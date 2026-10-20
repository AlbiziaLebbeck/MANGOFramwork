using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed class VoidDocumentHandler(CommerceDb db, ReceiptService receipts, Numbering numbering, CommerceOptions commerce, TimeProvider clock, ReceiptOptions? options = null) : ICommerceJobHandler
{
    public string Kind => "void-document.ensure";
    public async Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        using var payload = JsonDocument.Parse(payloadJson); var root = payload.RootElement;
        await EnsureAsync(root.GetProperty("orderId").ValueKind == JsonValueKind.Null ? null : root.GetProperty("orderId").GetGuid(), root.GetProperty("bankTransactionId").GetGuid(), root.GetProperty("source").GetString()!,
            root.GetProperty("evidenceReference").GetString()!, root.GetProperty("observedAt").GetDateTimeOffset(), cancellationToken);
    }

    public async Task<DocumentView> EnsureAsync(Guid? orderId, Guid bankId, string source, string evidenceReference, DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        var issuer = options ?? new ReceiptOptions(); issuer.AssertProductionReady(commerce.Environment);
        // An extra/unmatched credit is not a purchase receipt. Only a primary allocation merits an original receipt.
        var primaryBank = orderId is null ? null : await db.Allocations.AsNoTracking().Where(x => x.OrderId == orderId && x.Primary).Select(x => (Guid?)x.BankTransactionId).SingleOrDefaultAsync(cancellationToken);
        if (orderId is not null && primaryBank is not null) await receipts.EnsureAsync(orderId.Value, cancellationToken);
        db.ChangeTracker.Clear(); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lookup = await db.BankTransactions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == bankId && x.Environment == commerce.Environment, cancellationToken)
            ?? throw new CommerceException(404, "not_found", "The credit was not found.");
        var instructionLock = $"bank:{lookup.Environment}:{lookup.MerchantId}:{lookup.InstructionId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({instructionLock}, 0))", cancellationToken);
        var order = orderId is null ? null : (await db.Orders.FromSqlInterpolated($"SELECT * FROM ttb.\"Orders\" WHERE \"Id\" = {orderId.Value} FOR UPDATE").ToListAsync(cancellationToken)).Single();
        var existing = await db.Documents.SingleOrDefaultAsync(x => x.BankTransactionId == bankId && x.Kind == "Void", cancellationToken);
        if (existing is not null) return ReceiptService.View(existing);
        var bank = await db.BankTransactions.SingleOrDefaultAsync(x => x.Id == bankId && x.Voided, cancellationToken)
            ?? throw new CommerceException(409, "void_document_unconfirmed", "A void document requires confirmed bank void evidence.");
        var allocatedOrder = await db.Allocations.Where(x => x.BankTransactionId == bankId).Select(x => (Guid?)x.OrderId).SingleOrDefaultAsync(cancellationToken);
        var expectedOrder = allocatedOrder ?? await db.Attempts.Where(x => x.QrId == bank.QrId && x.MerchantId == bank.MerchantId).Select(x => (Guid?)x.OrderId).SingleOrDefaultAsync(cancellationToken);
        if (expectedOrder != orderId) throw new CommerceException(409, "void_document_order_mismatch", "The credit does not belong to the supplied order.");
        var originalDocument = orderId is null ? null : await db.Documents.SingleOrDefaultAsync(x => x.OrderId == orderId && x.Kind == "Receipt", cancellationToken);
        var original = originalDocument is null ? null : ReceiptTemplate.Read(originalDocument);
        var operation = await db.Voids.AsNoTracking().Where(x => x.BankTransactionId == bankId && x.State == "Confirmed").OrderByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        var now = clock.GetUtcNow(); var date = TimeZoneInfo.ConvertTime(now, Numbering.Bangkok).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var sequence = await numbering.NextAsync($"void-document:{date}", 999999, cancellationToken);
        var prefix = commerce.Environment switch { CommerceEnvironment.Production => "V", CommerceEnvironment.Uat => "TV", _ => "LV" };
        var info = new VoidDocumentSnapshot(primaryBank == bankId && original is not null ? original.Number : "ไม่มีใบเสร็จสำหรับยอดรับเงินนี้", bank.QrId, bank.Ref1, bank.Ref2, bank.AmountMinor, operation?.TransactionId,
            operation?.OperatorName, operation?.Reason ?? "ธนาคารยืนยันการยกเลิกรายการผ่านรายงาน", source, evidenceReference, observedAt);
        var buyer = original?.Buyer ?? (order is null ? new BuyerInput("ไม่ระบุผู้ซื้อ") : JsonSerializer.Deserialize<BuyerDeliverySnapshot>(order.BuyerSnapshotJson, CatalogService.Json)!.Buyer);
        var snapshot = new ReceiptSnapshot(1, commerce.Environment.ToString(), commerce.PaymentMode == PaymentMode.Mock, $"{prefix}{date}{sequence:D6}", order?.Number ?? "ไม่มีคำสั่งซื้อที่จับคู่",
            original?.Issuer ?? await new IssuerProfileService(db, commerce, issuer, clock).SnapshotAsync(cancellationToken), buyer, [], "THB", bank.AmountMinor, 0, bank.AmountMinor, bank.InstructionId, now, observedAt, info);
        var json = JsonSerializer.Serialize(snapshot, CatalogService.Json);
        var document = new CommerceDocument { OrderId = order?.Id, BankTransactionId = bank.Id, Kind = "Void", Number = snapshot.Number, TemplateVersion = "void-v1", SnapshotJson = json, SnapshotHash = SecretTokens.Hash(json), CreatedAt = now };
        db.Documents.Add(document); db.Jobs.Add(DurableWork.Enqueue($"render:{document.Id}:void-v1", "document.render", JsonSerializer.Serialize(new { documentId = document.Id })));
        db.AuditEvents.Add(new AuditEvent { Actor = "void-document-worker", Action = "void-document.issue", Resource = $"document:{document.Id}", DataJson = JsonSerializer.Serialize(new { bankId, source, evidenceReference, document.Number, document.SnapshotHash }) });
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return ReceiptService.View(document);
    }
}
