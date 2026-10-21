using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed record UatDiagnosticQr(string QrId, string Ref1, string Ref2, string Payload, string ImageDataUrl, DateTimeOffset ExpiresAt);

// Private operator command only. No shopper endpoint, order, credit allocation, delivery or fake success.
public static class UatBankDiagnostics
{
    public static void AssertProfile(CommerceOptions commerce, TtbOptions bank)
    {
        if (commerce.Environment != CommerceEnvironment.Uat || commerce.PaymentMode != PaymentMode.Bank ||
            !bank.QrProfileApproved || bank.MissingBankRequirements().Any(x => x is "Ttb.MerchantId" or "Ttb.BillerNo" or "Ttb.QrMerchantName"))
            throw new InvalidOperationException("Diagnostic QR requires the confirmed UAT bank QR profile.");
    }

    public static UatDiagnosticQr Build(TtbOptions bank, DateTimeOffset expiry, long sequence, Guid run)
    {
        if (sequence is < 1 or > 99999) throw new ArgumentOutOfRangeException(nameof(sequence));
        var local = TimeZoneInfo.ConvertTime(expiry, Numbering.Bangkok);
        var qrId = string.Create(CultureInfo.InvariantCulture, $"Z{bank.MerchantId}E{local:yyMMddHHmm}{sequence:D5}");
        var reference = run.ToString("N").ToUpperInvariant();
        var ref1 = "TTBUAT" + reference[..14];
        var ref2 = "TEST" + reference[14..30];
        var payload = ThaiQr.Build(new(bank.BillerNo, ref1, ref2, qrId, new Money(100), bank.QrMerchantName));
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(data);
        return new(qrId, ref1, ref2, payload, "data:image/png;base64," + Convert.ToBase64String(png.GetGraphic(8)), expiry);
    }

    public static async Task<object> RunAsync(CommerceDb db, Numbering numbering, CommerceOptions commerce, TtbOptions bank,
        BankReadiness readiness, IBankGateway gateway, TimeProvider clock, bool verify, CancellationToken cancellationToken)
    {
        AssertProfile(commerce, bank);
        if (verify) gateway.EnsureAvailable(BankOperation.Verify);
        var run = Guid.NewGuid();
        var now = clock.GetUtcNow();
        var expiry = DateTimeOffset.FromUnixTimeSeconds(now.AddMinutes(15).ToUnixTimeSeconds() / 60 * 60);
        // Share the exact counter used by checkout, preventing QRId collisions with real orders.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var local = TimeZoneInfo.ConvertTime(expiry, Numbering.Bangkok);
        var sequence = await numbering.NextAsync($"qr:{bank.MerchantId}:{local:yyyyMMddHHmm}", 99999, cancellationToken);
        var qr = Build(bank, expiry, sequence, run);
        await transaction.CommitAsync(cancellationToken);
        object? probe = null;
        if (verify)
        {
            // Exactly one read. Never poll, retry automatically or send Void from diagnostics.
            var uid = Guid.NewGuid();
            var reply = await gateway.PostAsync(BankOperation.Verify, new { bank.BillerNo, QRId = qr.QrId, TransDate = TtbBankGateway.BankTime(clock.GetUtcNow()) }, uid, cancellationToken);
            var result = BankReadResult.Parse(BankOperation.Verify, reply, bank.BillerNo, qr.QrId);
            probe = new { requestUid = uid, httpStatus = reply.StatusCode, reply.TransportError, result.Outcome, result.ResCode, result.Detail,
                authenticatedBankReply = result.Outcome is "Found" or "NotFound" or "AlreadyVoided", paymentConfirmed = false };
        }
        return new { status = "uat-bank-diagnostic", runId = run, environment = "Uat", merchantId = bank.MerchantId, billerNo = bank.BillerNo,
            issuer = bank.Issuer, amountMinor = 100, currency = "THB", issuedAt = now, qr,
            warning = "BANK TEST NETWORK ONLY. TTB scans this diagnostic QR. It is not a customer order; any credit requires unmatched-payment review. No receipt or delivery is issued.",
            checkoutReady = readiness.MissingForCheckout().Count == 0, missingCheckoutRequirements = readiness.MissingForCheckout(),
            bankAcceptanceComplete = readiness.Missing().Count == 0, missingBankAcceptanceRequirements = readiness.Missing(), probe,
            financialRecordsCreated = false, sequenceAllocated = true, bankRequestCount = verify ? 1 : 0 };
    }
}
