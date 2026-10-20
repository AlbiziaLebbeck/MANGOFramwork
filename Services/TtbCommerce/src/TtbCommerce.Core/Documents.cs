using System.Text.Json.Serialization;

namespace TtbCommerce.Core;

public sealed class ReceiptOptions
{
    public string IssuerName { get; set; } = "บริษัท MANGOs ทดสอบ จำกัด (ข้อมูลจำลอง)";
    public string IssuerAddress { get; set; } = "เลขที่ 1 ถนนทดสอบ แขวงทดสอบ เขตทดสอบ กรุงเทพมหานคร 10000 (ที่อยู่จำลอง)";
    public string TaxId { get; set; } = "0000000000000";
    public string Branch { get; set; } = "สำนักงานใหญ่ (ทดสอบ)";
    public string Contact { get; set; } = "test@example.invalid";
    public bool CompanyProfileApproved { get; set; }
    public string BrowserExecutablePath { get; set; } = "";

    public void AssertProductionReady(CommerceEnvironment environment)
    {
        if (environment == CommerceEnvironment.Production && (!CompanyProfileApproved || string.IsNullOrWhiteSpace(IssuerName) ||
            string.IsNullOrWhiteSpace(IssuerAddress) || TaxId.Length != 13 || TaxId == "0000000000000" || TaxId.Any(x => x is < '0' or > '9')))
            throw new InvalidOperationException("Production receipt issuer profile must be approved and complete.");
        if (IssuerName.Length > 200 || IssuerAddress.Length > 1000 || Branch.Length > 100 || Contact.Length > 300 || Branch.Any(char.IsControl) || Contact.Any(char.IsControl) || IssuerName.Any(char.IsControl) || IssuerAddress.Any(c => char.IsControl(c) && c is not '\n' and not '\r'))
            throw new InvalidOperationException("Receipt issuer profile is invalid.");
    }
}

public sealed class TestIssuerProfile : Entity
{
    public const string SingletonKey = "test-receipt-issuer";
    public string Key { get; set; } = SingletonKey;
    public long Version { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string TaxId { get; set; } = "";
    public string Branch { get; set; } = "";
    public string Contact { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed record IssuerProfileInput(long ExpectedVersion, string Name, string Address, string TaxId, string Branch, string Contact);
public sealed record IssuerProfileView(long Version, string Name, string Address, string TaxId, string Branch, string Contact, bool Editable, bool Approved);

public sealed class CommerceDocument : Entity
{
    public Guid? OrderId { get; set; }
    public Guid BankTransactionId { get; set; }
    public string Kind { get; set; } = "Receipt";
    public string Number { get; set; } = "";
    public string SnapshotJson { get; set; } = "{}";
    public string SnapshotHash { get; set; } = "";
    public string TemplateVersion { get; set; } = "receipt-v1";
    public string State { get; set; } = "Pending";
    public string? FileKey { get; set; }
    public string? FileSha256 { get; set; }
    public long? FileBytes { get; set; }
    public DateTimeOffset? ReadyAt { get; set; }
}

public sealed record ReceiptIssuer(string Name, string Address, string TaxId, bool Approved);
public sealed record ReceiptLine(string Name, string Variant, string Sku, string Kind, int Quantity, long UnitPriceMinor, long TotalMinor);
public sealed record ReceiptSnapshot(int SchemaVersion, string Environment, bool Simulated, string Number, string OrderNumber, ReceiptIssuer Issuer,
    BuyerInput Buyer, ReceiptLine[] Lines, string Currency, long SubtotalMinor, long ShippingMinor, long TotalMinor, string PaymentReference,
    DateTimeOffset IssuedAt, DateTimeOffset ConfirmedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] VoidDocumentSnapshot? Void = null);
public sealed record VoidDocumentSnapshot(string OriginalReceiptNumber, string QrId, string Ref1, string Ref2, long VoidAmountMinor,
    string? VoidTransactionId, string? OperatorName, string Reason, string EvidenceSource, string EvidenceReference, DateTimeOffset ObservedAt);
public sealed record DocumentView(Guid Id, string Kind, string Number, string State, string TemplateVersion, DateTimeOffset IssuedAt, DateTimeOffset? ReadyAt);
public sealed record DocumentDownload(Stream Stream, string FileName);
public sealed record OrderActivity(OrderView Order, FulfillmentView[] Fulfillment, DocumentView[] Documents, OrderPaymentConfirmation? Payment, OrderRefundSummary? Refund = null);
public sealed record OrderRefundSummary(string AmountMinor, string Source, DateTimeOffset? LatestApprovedAt, bool GoodsReviewPending, ReturnedLineInput[] ReturnedLines);
public sealed record OrderPaymentConfirmation(string Reference, DateTimeOffset? ConfirmedAt, bool Voided, bool OriginalBankPaymentTimeKnown);
