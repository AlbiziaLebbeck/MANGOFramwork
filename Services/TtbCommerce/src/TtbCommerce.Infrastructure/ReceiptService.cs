using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed class ReceiptEnsureHandler(ReceiptService receipts) : ICommerceJobHandler
{
    public string Kind => "receipt.ensure";
    public async Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(payloadJson);
        await receipts.EnsureAsync(document.RootElement.GetProperty("orderId").GetGuid(), cancellationToken);
    }
}

public sealed class ReceiptService(CommerceDb db, Numbering numbering, CommerceOptions commerce, ReceiptOptions options, TimeProvider clock, PrivateFiles files)
{
    public async Task<DocumentView> EnsureAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null) throw new InvalidOperationException("Receipt issuance must own its transaction.");
        db.ChangeTracker.Clear(); options.AssertProductionReady(commerce.Environment);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var order = (await db.Orders.FromSqlInterpolated($"SELECT * FROM ttb.\"Orders\" WHERE \"Id\" = {orderId} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault() ?? throw Missing();
        var existing = await db.Documents.SingleOrDefaultAsync(x => x.OrderId == orderId && x.Kind == "Receipt", cancellationToken);
        if (existing is not null) return View(existing);
        // Receipt proves an allocated credit. A subsequent hold does not erase the payment evidence.
        var bank = await (from a in db.Allocations join b in db.BankTransactions on a.BankTransactionId equals b.Id
                          where a.OrderId == orderId && a.Primary && a.AmountMinor == order.TotalMinor select b).SingleOrDefaultAsync(cancellationToken);
        if (bank is null || order.ConfirmedAt is null || bank.AmountMinor != order.TotalMinor)
            throw new CommerceException(409, "receipt_not_ready", "The order has no confirmed allocated payment.");
        var now = clock.GetUtcNow(); var date = TimeZoneInfo.ConvertTime(now, Numbering.Bangkok).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var sequence = await numbering.NextAsync($"receipt:{date}", 999999, cancellationToken);
        var prefix = commerce.Environment switch { CommerceEnvironment.Production => "R", CommerceEnvironment.Uat => "TR", _ => "LR" };
        var number = $"{prefix}{date}{sequence:D6}";
        var buyer = JsonSerializer.Deserialize<BuyerDeliverySnapshot>(order.BuyerSnapshotJson, CatalogService.Json) ?? throw new InvalidOperationException("Missing buyer snapshot.");
        var lines = await db.OrderLines.AsNoTracking().Where(x => x.OrderId == orderId).OrderBy(x => x.Id)
            .Select(x => new ReceiptLine(x.Name, x.VariantName, x.Sku, x.Kind.ToString(), x.Quantity, x.UnitPriceMinor, x.TotalMinor)).ToArrayAsync(cancellationToken);
        if (lines.Length == 0 || lines.Sum(x => x.TotalMinor) != order.SubtotalMinor) throw new InvalidOperationException("Receipt totals are inconsistent.");
        var snapshot = new ReceiptSnapshot(1, commerce.Environment.ToString(), commerce.PaymentMode == PaymentMode.Mock, number, order.Number,
            await new IssuerProfileService(db, commerce, options, clock).SnapshotAsync(cancellationToken), buyer.Buyer, lines, order.Currency,
            order.SubtotalMinor, order.ShippingMinor, order.TotalMinor, bank.InstructionId, now, order.ConfirmedAt.Value);
        var json = JsonSerializer.Serialize(snapshot, CatalogService.Json);
        var document = new CommerceDocument { OrderId = orderId, BankTransactionId = bank.Id, Number = number, SnapshotJson = json, SnapshotHash = SecretTokens.Hash(json), CreatedAt = now };
        db.Documents.Add(document);
        db.Jobs.Add(DurableWork.Enqueue($"render:{document.Id}:receipt-v1", "document.render", JsonSerializer.Serialize(new { documentId = document.Id })));
        db.AuditEvents.Add(new AuditEvent { Actor = "worker", Action = "receipt.issue", Resource = $"document:{document.Id}", DataJson = JsonSerializer.Serialize(new { number, document.SnapshotHash }) });
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return View(document);
    }

    public async Task<DocumentView[]> OwnAsync(Guid shopperId, Guid orderId, CancellationToken cancellationToken)
    {
        if (!await db.Orders.AnyAsync(x => x.Id == orderId && x.ShopperId == shopperId, cancellationToken)) throw Missing();
        return (await db.Documents.AsNoTracking().Where(x => x.OrderId == orderId).OrderBy(x => x.Id).ToArrayAsync(cancellationToken)).Select(View).ToArray();
    }

    public async Task<DocumentDownload> DownloadAsync(Guid? shopperId, Guid documentId, string actor, string requestUid, CancellationToken cancellationToken)
    {
        var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == documentId &&
            (shopperId == null || db.Orders.Any(o => o.Id == d.OrderId && o.ShopperId == shopperId)), cancellationToken) ?? throw Missing();
        if (document.State != "Ready" || document.FileKey is null) throw new CommerceException(409, "document_pending", "The document is being prepared. Try again shortly.");
        var path = files.PathFor("documents", document.FileKey, ".pdf");
        FileStream stream;
        try { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous); }
        catch (IOException) { throw Unavailable(); }
        try
        {
            if (stream.Length != document.FileBytes || Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken)) != document.FileSha256) throw Unavailable();
            stream.Position = 0;
            db.AuditEvents.Add(new AuditEvent { Actor = actor, Action = "document.download-request", Resource = $"document:{documentId}", CorrelationId = requestUid });
            await db.SaveChangesAsync(cancellationToken); return new(stream, document.Number + ".pdf");
        }
        catch { await stream.DisposeAsync(); throw; }
    }

    public static DocumentView View(CommerceDocument document) => new(document.Id, document.Kind, document.Number, document.State, document.TemplateVersion, document.CreatedAt, document.ReadyAt);
    private static CommerceException Missing() => new(404, "not_found", "The document or order was not found.");
    private static CommerceException Unavailable() => new(503, "document_unavailable", "The stored document is temporarily unavailable.");
}
