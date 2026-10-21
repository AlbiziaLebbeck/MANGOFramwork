using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed record PaymentQrView(Guid AttemptId, string QrId, string Payload, string ImageDataUrl, string Mode, string BeneficiaryName, long AmountMinor, string Currency,
    DateTimeOffset ExpiresAt, DateTimeOffset IssuedAt, DateTimeOffset VerificationAvailableAt, int ChecksRemaining);

public sealed class QrService(CommerceDb db, Numbering numbering, CommerceOptions commerce, TtbOptions ttb, BankReadiness readiness, TimeProvider clock)
{
    public async Task<PaymentQrView> GetOrCreateAsync(Guid shopperId, Guid orderId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var order = (await db.Orders.FromSqlInterpolated($"SELECT * FROM ttb.\"Orders\" WHERE \"Id\" = {orderId} AND \"ShopperId\" = {shopperId} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault()
            ?? throw new CommerceException(404, "not_found", "The order was not found.");
        if (order.State != OrderState.Open || order.PaymentState != PaymentState.Unpaid)
            throw new CommerceException(409, "order_not_payable", "This order is no longer awaiting payment.");
        readiness.AssertCheckoutReady();
        var existing = await db.Attempts.SingleOrDefaultAsync(x => x.OrderId == orderId && x.State == AttemptState.AwaitingPayment, cancellationToken);
        if (existing is not null)
        {
            var expectedProfile = commerce.PaymentMode == PaymentMode.Mock ? "MOCK-v1" : "TTB-BPQR-ZE-v1";
            if (existing.Profile != expectedProfile || commerce.PaymentMode == PaymentMode.Bank && (existing.MerchantId != ttb.MerchantId || existing.BillerNo != ttb.BillerNo))
                throw new CommerceException(409, "attempt_profile_changed", "This QR belongs to another payment profile. Please contact support.");
            if (existing.ExpiresAt <= clock.GetUtcNow()) throw Expired();
            await transaction.CommitAsync(cancellationToken);
            return View(existing);
        }
        var expires = DateTimeOffset.FromUnixTimeSeconds(order.ExpiresAt.ToUnixTimeSeconds() / 60 * 60);
        if (expires <= clock.GetUtcNow().AddSeconds(60)) throw Expired();
        var merchant = commerce.PaymentMode == PaymentMode.Mock ? "SIM" : ttb.MerchantId;
        var biller = commerce.PaymentMode == PaymentMode.Mock ? "000000000000000" : ttb.BillerNo;
        var localExpiry = TimeZoneInfo.ConvertTime(expires, Numbering.Bangkok);
        var running = await numbering.NextAsync($"qr:{merchant}:{localExpiry:yyyyMMddHHmm}", 99999, cancellationToken);
        var qrId = string.Create(CultureInfo.InvariantCulture, $"Z{merchant}E{localExpiry:yyMMddHHmm}{running:D5}");
        var attempt = new PaymentAttempt { OrderId = order.Id, MerchantId = merchant, BillerNo = biller, QrId = qrId, Ref1 = order.Number,
            Ref2 = "M" + order.Id.ToString("N")[..19].ToUpperInvariant(), ExpectedMinor = order.TotalMinor, ExpiresAt = expires,
            Profile = commerce.PaymentMode == PaymentMode.Mock ? "MOCK-v1" : "TTB-BPQR-ZE-v1" };
        // A mock QR must never be a valid bill-payment payload or use the user's real Biller.
        attempt.Payload = commerce.PaymentMode == PaymentMode.Mock ? $"MANGO-MOCK:{attempt.Id:N}" :
            ThaiQr.Build(new(biller, attempt.Ref1, attempt.Ref2, qrId, new Money(order.TotalMinor), ttb.QrMerchantName));
        attempt.PayloadHash = SecretTokens.Hash(attempt.Payload);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return View(attempt);
    }

    private PaymentQrView View(PaymentAttempt attempt)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(attempt.Payload, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(data);
        var image = "data:image/png;base64," + Convert.ToBase64String(png.GetGraphic(8));
        return new(attempt.Id, attempt.QrId, attempt.Payload, image, commerce.PaymentMode.ToString(), commerce.PaymentMode == PaymentMode.Mock ? "บัญชีจำลอง — ไม่มีการรับเงินจริง" : ttb.BeneficiaryName,
            attempt.ExpectedMinor, "THB", attempt.ExpiresAt, attempt.CreatedAt, attempt.CreatedAt.AddSeconds(commerce.VerificationDelaySeconds), Math.Max(0, commerce.VerificationBudget - attempt.VerificationCount));
    }

    private static CommerceException Expired() => new(409, "order_expiring", "This payment window has ended or is too short. Start a new checkout.");
}
