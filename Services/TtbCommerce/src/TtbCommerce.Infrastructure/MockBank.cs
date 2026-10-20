using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed record MockPaymentInput(string RequestKey, string Scenario = "success");
public sealed record MockPaymentView(string Mode, string InstructionId, string Outcome, string? AckCode = null, string? DuplicateAckCode = null);

public sealed class MockBankService(CommerceDb db, CommerceOptions options, PaymentProcessor payments, TimeProvider clock)
{
    public async Task<MockPaymentView> PayAsync(Guid shopperId, Guid orderId, MockPaymentInput input, CancellationToken cancellationToken)
    {
        Guard(options);
        if (!Regex.IsMatch(input.RequestKey ?? "", "^[A-Za-z0-9:_-]{1,100}$", RegexOptions.CultureInvariant) ||
            input.Scenario is not ("success" or "lost-callback" or "duplicate" or "amount-mismatch" or "late" or "extra-credit"))
            throw new CommerceException(400, "mock_input_invalid", "Choose a supported local simulation scenario and request key.");
        string noticeJson; string scenario;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var order = (await db.Orders.FromSqlInterpolated($"SELECT * FROM ttb.\"Orders\" WHERE \"Id\" = {orderId} AND \"ShopperId\" = {shopperId} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault()
                ?? throw new CommerceException(404, "not_found", "The order was not found.");
            var attempt = await db.Attempts.SingleOrDefaultAsync(x => x.OrderId == orderId && x.Profile == "MOCK-v1", cancellationToken)
                ?? throw new CommerceException(409, "mock_attempt_required", "Issue a local mock QR first.");
            var existing = await db.MockCredits.SingleOrDefaultAsync(x => x.OrderId == orderId && x.RequestKey == input.RequestKey, cancellationToken);
            if (existing is not null)
            {
                if (existing.Scenario != input.Scenario) throw new CommerceException(409, "idempotency_conflict", "This simulation key already refers to another scenario.");
                noticeJson = existing.NoticeJson; scenario = existing.Scenario;
            }
            else
            {
                if (input.Scenario is not ("late" or "extra-credit") && (order.State != OrderState.Open || order.PaymentState != PaymentState.Unpaid))
                    throw new CommerceException(409, "order_not_payable", "This order is no longer awaiting payment.");
                var amount = input.Scenario == "amount-mismatch" ? checked(attempt.ExpectedMinor + 1) : attempt.ExpectedMinor;
                var notice = new CreditNotice("MOCK" + Guid.NewGuid().ToString("N"), attempt.BillerNo, attempt.Ref1, attempt.Ref2, attempt.QrId, new Money(amount).ToString(),
                    "000", "Simulated credit", TtbBankGateway.BankTime(clock.GetUtcNow()), PayerName: "ผู้ชำระจำลอง");
                noticeJson = JsonSerializer.Serialize(notice, BankWire.Json); scenario = input.Scenario;
                db.MockCredits.Add(new MockCredit { OrderId = orderId, AttemptId = attempt.Id, RequestKey = input.RequestKey!, Scenario = scenario, NoticeJson = noticeJson });
                db.AuditEvents.Add(new AuditEvent { Actor = $"shopper:{shopperId}", Action = "mock.credit", Resource = $"order:{orderId}", DataJson = JsonSerializer.Serialize(new { scenario }) });
                await db.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        var raw = Encoding.UTF8.GetBytes(noticeJson); var parsed = BankWire.ParseNotice(raw);
        if (scenario == "lost-callback") return new("Mock", parsed.InstructionId, "CreditAtSimulatorOnly");
        var result = await payments.NoticeAsync(raw, new(EvidenceSource.Mock, Guid.NewGuid().ToString("D")), cancellationToken);
        CreditApplication? duplicate = null;
        if (scenario == "duplicate") duplicate = await payments.NoticeAsync(raw, new(EvidenceSource.Mock, Guid.NewGuid().ToString("D")), cancellationToken);
        return new("Mock", parsed.InstructionId, result.Outcome, result.Ack.ResCode, duplicate?.Ack.ResCode);
    }

    public static void Guard(CommerceOptions options)
    {
        if (options.Environment != CommerceEnvironment.Local || options.PaymentMode != PaymentMode.Mock)
            throw new CommerceException(404, "not_found", "The simulation is available only in local mock mode.");
    }
}

public sealed class MockBankGateway(CommerceDb db, CommerceOptions options, TimeProvider clock) : IBankGateway
{
    public void EnsureAvailable() => MockBankService.Guard(options);
    public async Task<BankReply> PostAsync(BankOperation operation, object body, Guid requestUid, CancellationToken cancellationToken)
    {
        EnsureAvailable();
        if (operation is BankOperation.MerchantVoid or BankOperation.EnquiryVoid) return await VoidAsync(operation, body, cancellationToken);
        if (operation is not BankOperation.Verify and not BankOperation.EnquiryRange) throw new CommerceException(409, "mock_operation_unavailable", "This simulation operation is not implemented.");
        var request = JsonSerializer.SerializeToElement(body, BankWire.Json);
        var biller = request.GetProperty("BillerNo").GetString(); var qr = request.GetProperty("QRId").GetString();
        var attempts = await db.Attempts.AsNoTracking().Where(x => x.BillerNo == biller && x.QrId == qr && x.Profile == "MOCK-v1").Select(x => x.Id).ToArrayAsync(cancellationToken);
        var credits = await db.MockCredits.AsNoTracking().Where(x => attempts.Contains(x.AttemptId)).OrderBy(x => x.CreatedAt).ToArrayAsync(cancellationToken);
        var now = TtbBankGateway.BankTime(clock.GetUtcNow());
        JsonObject response;
        if (operation == BankOperation.Verify && credits.Length > 0)
        {
            response = JsonNode.Parse(credits[0].NoticeJson)!.AsObject(); response["ResultCode"] = "0000"; response["ResCode"] = "0000"; response["ResDesc"] = "Simulated success";
        }
        else
        {
            response = new() { ["ResCode"] = credits.Length > 0 ? "0000" : operation == BankOperation.Verify ? "0002" : "0001", ["ResDesc"] = "Simulation",
                ["BillerNo"] = biller, ["QRId"] = qr, ["TransDate"] = now };
            if (credits.Length > 0) response["QTransaction"] = new JsonArray(credits.Select(x => JsonNode.Parse(x.NoticeJson)).ToArray());
        }
        return new(200, JsonSerializer.SerializeToUtf8Bytes(response));
    }

    private async Task<BankReply> VoidAsync(BankOperation operation, object body, CancellationToken cancellationToken)
    {
        EnsureAvailable(); var request = JsonSerializer.SerializeToElement(body, BankWire.Json);
        var instruction = request.GetProperty("InstructionId").GetString()!; var transactionId = request.GetProperty("TransactionId").GetString()!;
        var qrId = request.GetProperty("QRId").GetString()!; var biller = request.GetProperty("BillerNo").GetString()!;
        db.ChangeTracker.Clear(); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var key = "mock-void:" + instruction; await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        // Only a committed Local void intent can reach this simulator, and it never contacts a real bank.
        if (!await db.Voids.AnyAsync(x => x.Environment == CommerceEnvironment.Local && x.MerchantId == "SIM" && x.TransactionId == transactionId &&
            x.InstructionId == instruction && x.QrId == qrId && x.BillerNo == biller && x.SendFencedAt != null, cancellationToken)) throw new CommerceException(409, "mock_void_intent_required", "A fenced local void intent is required.");
        var existing = await db.MockVoids.SingleOrDefaultAsync(x => x.InstructionId == instruction, cancellationToken);
        string code;
        if (operation == BankOperation.EnquiryVoid) code = existing is not null && existing.QrId == qrId && existing.BillerNo == biller ? "0000" : "0003";
        else if (existing is not null) code = existing.QrId == qrId && existing.BillerNo == biller ? "0002" : "0004";
        else if (!await db.BankTransactions.AnyAsync(x => x.Environment == CommerceEnvironment.Local && x.MerchantId == "SIM" && x.InstructionId == instruction && x.QrId == qrId && x.BillerNo == biller, cancellationToken)) code = "0003";
        else
        {
            db.MockVoids.Add(new MockVoidCredit { InstructionId = instruction, TransactionId = transactionId, BillerNo = biller, QrId = qrId, CreatedAt = clock.GetUtcNow() });
            code = "0000"; await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new(200, JsonSerializer.SerializeToUtf8Bytes(new { TransactionId = transactionId, InstructionId = instruction, QRId = qrId, BillerNo = biller, ResCode = code,
            ResDesc = "Local simulated void only", TransDate = TtbBankGateway.BankTime(clock.GetUtcNow()) }, BankWire.Json));
    }
}
