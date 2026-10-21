using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public sealed class CommerceDb(DbContextOptions<CommerceDb> options) : DbContext(options)
{
    public DbSet<DeploymentIdentity> Deployments => Set<DeploymentIdentity>();
    public DbSet<BankTestCase> BankTestCases => Set<BankTestCase>();
    public DbSet<BankTestEvent> BankTestEvents => Set<BankTestEvent>();
    public DbSet<BankTestReceipt> BankTestReceipts => Set<BankTestReceipt>();
    public DbSet<BankTestOperation> BankTestOperations => Set<BankTestOperation>();
    public DbSet<Shopper> Shoppers => Set<Shopper>();
    public DbSet<ShopperSession> Sessions => Set<ShopperSession>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> Variants => Set<ProductVariant>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartLine> CartLines => Set<CartLine>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<StockReservation> Reservations => Set<StockReservation>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<PaymentAttempt> Attempts => Set<PaymentAttempt>();
    public DbSet<BankTransaction> BankTransactions => Set<BankTransaction>();
    public DbSet<PaymentObservation> Observations => Set<PaymentObservation>();
    public DbSet<CallbackReceipt> CallbackReceipts => Set<CallbackReceipt>();
    public DbSet<PaymentAllocation> Allocations => Set<PaymentAllocation>();
    public DbSet<VerificationRequest> VerificationRequests => Set<VerificationRequest>();
    public DbSet<ReconciliationCase> Cases => Set<ReconciliationCase>();
    public DbSet<OutboxJob> Jobs => Set<OutboxJob>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<NumberSequence> Sequences => Set<NumberSequence>();
    public DbSet<MockCredit> MockCredits => Set<MockCredit>();
    public DbSet<FulfillmentItem> Fulfillments => Set<FulfillmentItem>();
    public DbSet<DigitalAsset> DigitalAssets => Set<DigitalAsset>();
    public DbSet<Entitlement> Entitlements => Set<Entitlement>();
    public DbSet<DigitalDownloadEvent> DownloadEvents => Set<DigitalDownloadEvent>();
    public DbSet<EntitlementOperation> EntitlementOperations => Set<EntitlementOperation>();
    public DbSet<CommerceDocument> Documents => Set<CommerceDocument>();
    public DbSet<TestIssuerProfile> TestIssuerProfiles => Set<TestIssuerProfile>();
    public DbSet<GameUseTicket> GameTickets => Set<GameUseTicket>();
    public DbSet<ReportArchive> Reports => Set<ReportArchive>();
    public DbSet<ReportRow> ReportRows => Set<ReportRow>();
    public DbSet<ReportFetch> ReportFetches => Set<ReportFetch>();
    public DbSet<VoidOperation> Voids => Set<VoidOperation>();
    public DbSet<VoidBankRequest> VoidRequests => Set<VoidBankRequest>();
    public DbSet<MockVoidCredit> MockVoids => Set<MockVoidCredit>();
    public DbSet<FinanceCaseNote> CaseNotes => Set<FinanceCaseNote>();
    public DbSet<FinanceOperation> FinanceOperations => Set<FinanceOperation>();
    public DbSet<ManualRefund> ManualRefunds => Set<ManualRefund>();
    public DbSet<FinanceEvidenceFile> FinanceEvidence => Set<FinanceEvidenceFile>();
    public DbSet<GoodsReturnLine> GoodsReturns => Set<GoodsReturnLine>();
    public DbSet<GoodsUnitDisposition> GoodsUnits => Set<GoodsUnitDisposition>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("ttb");
        model.Entity<BankTestCase>().ToTable("BankTestCases", t => t.HasCheckConstraint("CK_BankTestCases_Amount", "\"AmountMinor\" > 0"));
        model.Entity<BankTestCase>().HasIndex(x => x.QrId).IsUnique();
        model.Entity<BankTestEvent>().ToTable("BankTestEvents");
        model.Entity<BankTestEvent>().HasIndex(x => new { x.CaseId, x.Id });
        model.Entity<BankTestEvent>().HasIndex(x => new { x.Kind, x.Id });
        model.Entity<BankTestEvent>().HasOne<BankTestCase>().WithMany().HasForeignKey(x => x.CaseId);
        model.Entity<BankTestEvent>().HasOne<BankTestOperation>().WithMany().HasForeignKey(x => x.OperationId);
        model.Entity<BankTestReceipt>().ToTable("BankTestReceipts");
        model.Entity<BankTestReceipt>().HasIndex(x => new { x.MerchantId, x.InstructionId }).IsUnique();
        model.Entity<BankTestReceipt>().HasOne<BankTestCase>().WithMany().HasForeignKey(x => x.CaseId);
        model.Entity<BankTestOperation>().ToTable("BankTestOperations");
        model.Entity<BankTestOperation>().HasIndex(x => x.OperationKey).IsUnique();
        model.Entity<BankTestOperation>().HasIndex(x => new { x.CaseId, x.Kind });
        model.Entity<BankTestOperation>().HasOne<BankTestCase>().WithMany().HasForeignKey(x => x.CaseId);
        model.Entity<TestIssuerProfile>().HasIndex(x => x.Key).IsUnique();
        model.Entity<TestIssuerProfile>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<TestIssuerProfile>().Property(x => x.Name).HasMaxLength(200);
        model.Entity<TestIssuerProfile>().Property(x => x.Address).HasMaxLength(1000);
        model.Entity<TestIssuerProfile>().Property(x => x.TaxId).HasMaxLength(13);
        model.Entity<TestIssuerProfile>().Property(x => x.Branch).HasMaxLength(100);
        model.Entity<TestIssuerProfile>().Property(x => x.Contact).HasMaxLength(300);
        model.Entity<ManualRefund>().ToTable("ManualRefunds", table => table.HasCheckConstraint("CK_ManualRefunds_Amount", "\"AmountMinor\" > 0"));
        model.Entity<ManualRefund>().Property(x => x.Version).HasDefaultValue(1L).IsConcurrencyToken();
        model.Entity<ManualRefund>().HasIndex(x => x.CaseId).IsUnique();
        model.Entity<ManualRefund>().HasIndex(x => new { x.BankTransactionId, x.State });
        model.Entity<ManualRefund>().HasOne<BankTransaction>().WithMany().HasForeignKey(x => x.BankTransactionId);
        model.Entity<ManualRefund>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId);
        model.Entity<ManualRefund>().HasOne<ReconciliationCase>().WithMany().HasForeignKey(x => x.CaseId);
        model.Entity<ManualRefund>().HasOne<ReconciliationCase>().WithMany().HasForeignKey(x => x.SourceCaseId);
        model.Entity<FinanceEvidenceFile>().ToTable("FinanceEvidence");
        model.Entity<FinanceEvidenceFile>().HasOne<ReconciliationCase>().WithMany().HasForeignKey(x => x.CaseId);
        model.Entity<FinanceEvidenceFile>().HasIndex(x => x.Key).IsUnique();
        model.Entity<FinanceEvidenceFile>().HasIndex(x => new { x.CaseId, x.Sha256 }).IsUnique();
        model.Entity<FinanceEvidenceFile>().HasIndex(x => new { x.CaseId, x.Id });
        model.Entity<GoodsReturnLine>().ToTable("GoodsReturns", table => table.HasCheckConstraint("CK_GoodsReturns_Quantity", "\"Quantity\" > 0"));
        model.Entity<GoodsReturnLine>().HasIndex(x => x.EffectKey).IsUnique();
        model.Entity<GoodsReturnLine>().HasIndex(x => x.OrderLineId);
        model.Entity<GoodsReturnLine>().HasOne<ReconciliationCase>().WithMany().HasForeignKey(x => x.CaseId);
        model.Entity<GoodsReturnLine>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId);
        model.Entity<GoodsReturnLine>().HasOne<OrderLine>().WithMany().HasForeignKey(x => x.OrderLineId);
        model.Entity<GoodsUnitDisposition>().ToTable("GoodsUnits", table => table.HasCheckConstraint("CK_GoodsUnits_Index", "\"UnitIndex\" >= 0"));
        model.Entity<GoodsUnitDisposition>().HasIndex(x => new { x.OrderLineId, x.UnitIndex }).IsUnique();
        model.Entity<GoodsUnitDisposition>().HasOne<ReconciliationCase>().WithMany().HasForeignKey(x => x.CaseId);
        model.Entity<GoodsUnitDisposition>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId);
        model.Entity<GoodsUnitDisposition>().HasOne<OrderLine>().WithMany().HasForeignKey(x => x.OrderLineId);
        model.Entity<GoodsUnitDisposition>().HasOne<Entitlement>().WithMany().HasForeignKey(x => x.EntitlementId);
        model.Entity<FinanceCaseNote>().ToTable("CaseNotes");
        model.Entity<FinanceCaseNote>().HasOne<ReconciliationCase>().WithMany().HasForeignKey(x => x.CaseId);
        model.Entity<FinanceCaseNote>().HasIndex(x => new { x.CaseId, x.Id });
        model.Entity<FinanceCaseNote>().Property(x => x.Body).HasMaxLength(2000);
        model.Entity<FinanceOperation>().ToTable("FinanceOperations");
        model.Entity<FinanceOperation>().HasIndex(x => x.OperationKey).IsUnique();
        model.Entity<FinanceOperation>().HasOne<ReconciliationCase>().WithMany().HasForeignKey(x => x.CaseId);
        model.Entity<FinanceOperation>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId);
        model.Entity<ReconciliationCase>().Property(x => x.Version).IsConcurrencyToken().HasDefaultValue(1L);
        model.Entity<MockVoidCredit>().ToTable("MockVoids");
        model.Entity<MockVoidCredit>().HasIndex(x => x.InstructionId).IsUnique();
        model.Entity<MockVoidCredit>().HasIndex(x => x.TransactionId).IsUnique();
        model.Entity<VoidOperation>().ToTable("Voids", t => t.HasCheckConstraint("CK_Voids_AmountCount", "\"AmountMinor\" > 0 AND \"EnquiryCount\" BETWEEN 0 AND 3"));
        model.Entity<VoidOperation>().HasIndex(x => new { x.Environment, x.MerchantId, x.TransactionId }).IsUnique();
        model.Entity<VoidOperation>().HasIndex(x => x.OperationKey).IsUnique();
        model.Entity<VoidOperation>().Property(x => x.AffectsOrderPayment).HasDefaultValue(true);
        model.Entity<VoidOperation>().HasIndex(x => x.BankTransactionId).IsUnique().HasFilter("\"State\" <> 'Rejected'");
        model.Entity<VoidOperation>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId);
        model.Entity<VoidOperation>().HasOne<BankTransaction>().WithMany().HasForeignKey(x => x.BankTransactionId);
        model.Entity<VoidBankRequest>().ToTable("VoidRequests");
        model.Entity<VoidBankRequest>().HasIndex(x => x.RequestUid).IsUnique();
        model.Entity<VoidBankRequest>().HasOne<VoidOperation>().WithMany().HasForeignKey(x => x.VoidOperationId);
        model.Entity<ReportFetch>().ToTable("ReportFetches");
        model.Entity<ReportFetch>().HasIndex(x => new { x.Environment, x.MerchantId, x.FileName }).IsUnique();
        model.Entity<ReportFetch>().HasOne<ReportArchive>().WithMany().HasForeignKey(x => x.ReportArchiveId);
        model.Entity<ReportArchive>().ToTable("Reports", t => t.HasCheckConstraint("CK_Reports_SizeRevision", "\"EncryptedBytes\" > 0 AND \"Revision\" > 0"));
        model.Entity<ReportArchive>().HasIndex(x => new { x.Environment, x.MerchantId, x.FileName, x.EncryptedSha256 }).IsUnique();
        model.Entity<ReportArchive>().HasIndex(x => new { x.Environment, x.MerchantId, x.FileName, x.Revision }).IsUnique();
        model.Entity<ReportArchive>().HasIndex(x => new { x.State, x.CreatedAt });
        model.Entity<ReportArchive>().HasOne<ReportArchive>().WithMany().HasForeignKey(x => x.ReplayOfId);
        model.Entity<ReportRow>().ToTable("ReportRows");
        model.Entity<ReportRow>().HasIndex(x => new { x.ReportArchiveId, x.Sequence }).IsUnique();
        model.Entity<ReportRow>().HasIndex(x => new { x.Outcome, x.Id });
        model.Entity<ReportRow>().HasOne<ReportArchive>().WithMany().HasForeignKey(x => x.ReportArchiveId);
        model.Entity<ReportRow>().HasOne<BankTransaction>().WithMany().HasForeignKey(x => x.BankTransactionId);
        model.Entity<GameUseTicket>().ToTable("GameTickets");
        model.Entity<GameUseTicket>().HasIndex(x => x.TokenHash).IsUnique();
        model.Entity<GameUseTicket>().HasIndex(x => new { x.ShopperId, x.CreatedAt });
        model.Entity<GameUseTicket>().HasIndex(x => x.ExpiresAt);
        model.Entity<GameUseTicket>().HasOne<Shopper>().WithMany().HasForeignKey(x => x.ShopperId);
        model.Entity<GameUseTicket>().HasOne<ShopperSession>().WithMany().HasForeignKey(x => x.SessionId);
        model.Entity<GameUseTicket>().HasOne<Entitlement>().WithMany().HasForeignKey(x => x.EntitlementId);
        model.Entity<CommerceDocument>().ToTable("Documents", t => t.HasCheckConstraint("CK_Documents_Ready", "\"State\" <> 'Ready' OR (\"FileKey\" IS NOT NULL AND \"FileSha256\" IS NOT NULL AND \"FileBytes\" > 0 AND \"ReadyAt\" IS NOT NULL)"));
        model.Entity<CommerceDocument>().HasIndex(x => new { x.OrderId, x.Kind }).IsUnique().HasFilter("\"Kind\" = 'Receipt'");
        model.Entity<CommerceDocument>().HasIndex(x => new { x.BankTransactionId, x.Kind }).IsUnique();
        model.Entity<CommerceDocument>().HasIndex(x => x.Number).IsUnique();
        model.Entity<CommerceDocument>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId);
        model.Entity<CommerceDocument>().HasOne<BankTransaction>().WithMany().HasForeignKey(x => x.BankTransactionId);
        model.Entity<DeploymentIdentity>().ToTable("Deployments");
        model.Entity<Shopper>().ToTable("Shoppers");
        model.Entity<ShopperSession>().ToTable("Sessions");
        model.Entity<Product>().ToTable("Products");
        model.Entity<ProductVariant>().ToTable("Variants", t =>
        {
            t.HasCheckConstraint("CK_Variants_Price", "\"PriceMinor\" >= 0");
            t.HasCheckConstraint("CK_Variants_Stock", "\"OnHand\" >= 0 AND \"Reserved\" >= 0 AND \"Reserved\" <= \"OnHand\"");
        });
        model.Entity<Cart>().ToTable("Carts");
        model.Entity<CartLine>().ToTable("CartLines", t => t.HasCheckConstraint("CK_CartLines_Quantity", "\"Quantity\" BETWEEN 1 AND 99"));
        model.Entity<Quote>().ToTable("Quotes", t => t.HasCheckConstraint("CK_Quotes_Total", "\"SubtotalMinor\" >= 0 AND \"ShippingMinor\" >= 0 AND \"TotalMinor\" = \"SubtotalMinor\" + \"ShippingMinor\""));
        model.Entity<Order>().ToTable("Orders", t => t.HasCheckConstraint("CK_Orders_Total", "\"SubtotalMinor\" >= 0 AND \"ShippingMinor\" >= 0 AND \"TotalMinor\" > 0 AND \"TotalMinor\" = \"SubtotalMinor\" + \"ShippingMinor\""));
        model.Entity<OrderLine>().ToTable("OrderLines", t => t.HasCheckConstraint("CK_OrderLines_Total", "\"Quantity\" BETWEEN 1 AND 99 AND \"UnitPriceMinor\" >= 0 AND \"TotalMinor\" = \"Quantity\"::bigint * \"UnitPriceMinor\""));
        model.Entity<StockReservation>().ToTable("Reservations", t => t.HasCheckConstraint("CK_Reservations_Quantity", "\"Quantity\" > 0"));
        model.Entity<StockMovement>().ToTable("StockMovements");
        model.Entity<PaymentAttempt>().ToTable("Attempts", t => t.HasCheckConstraint("CK_Attempts_Amount", "\"ExpectedMinor\" > 0 AND \"VerificationCount\" BETWEEN 0 AND 3"));
        model.Entity<BankTransaction>().ToTable("BankTransactions", t => t.HasCheckConstraint("CK_BankTransactions_Amount", "\"AmountMinor\" >= 0"));
        model.Entity<PaymentObservation>().ToTable("Observations");
        model.Entity<CallbackReceipt>().ToTable("CallbackReceipts");
        model.Entity<PaymentAllocation>().ToTable("Allocations", t => t.HasCheckConstraint("CK_Allocations_Amount", "\"AmountMinor\" > 0"));
        model.Entity<VerificationRequest>().ToTable("VerificationRequests");
        model.Entity<ReconciliationCase>().ToTable("Cases");
        model.Entity<OutboxJob>().ToTable("Jobs");
        model.Entity<AuditEvent>().ToTable("AuditEvents");
        model.Entity<NumberSequence>().ToTable("Sequences");
        model.Entity<MockCredit>().ToTable("MockCredits");
        model.Entity<FulfillmentItem>().ToTable("Fulfillments", t => t.HasCheckConstraint("CK_Fulfillments_Quantity", "\"Quantity\" > 0 AND \"Version\" > 0"));
        model.Entity<FulfillmentItem>().HasIndex(x => x.OrderLineId).IsUnique();
        model.Entity<FulfillmentItem>().HasIndex(x => new { x.Kind, x.State, x.Id });
        model.Entity<FulfillmentItem>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId);
        model.Entity<FulfillmentItem>().HasOne<OrderLine>().WithMany().HasForeignKey(x => x.OrderLineId);
        model.Entity<FulfillmentItem>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<DigitalAsset>().ToTable("DigitalAssets", t => t.HasCheckConstraint("CK_DigitalAssets_Size", "\"ByteLength\" > 0"));
        model.Entity<DigitalAsset>().HasAlternateKey(x => x.Key);
        model.Entity<Entitlement>().ToTable("Entitlements", t => t.HasCheckConstraint("CK_Entitlements_Unit", "\"UnitIndex\" BETWEEN 0 AND 98 AND \"DownloadRequests\" >= 0"));
        model.Entity<Entitlement>().HasIndex(x => new { x.OrderLineId, x.UnitIndex }).IsUnique();
        model.Entity<Entitlement>().HasIndex(x => new { x.ShopperId, x.Id });
        model.Entity<Entitlement>().HasOne<Shopper>().WithMany().HasForeignKey(x => x.ShopperId);
        model.Entity<Entitlement>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId);
        model.Entity<Entitlement>().HasOne<OrderLine>().WithMany().HasForeignKey(x => x.OrderLineId);
        model.Entity<Entitlement>().HasOne<DigitalAsset>().WithMany().HasForeignKey(x => x.DigitalAssetKey).HasPrincipalKey(x => x.Key);
        model.Entity<DigitalDownloadEvent>().ToTable("DownloadEvents");
        model.Entity<DigitalDownloadEvent>().HasOne<Entitlement>().WithMany().HasForeignKey(x => x.EntitlementId);
        model.Entity<DigitalDownloadEvent>().HasIndex(x => new { x.EntitlementId, x.CreatedAt });
        model.Entity<EntitlementOperation>().ToTable("EntitlementOperations");
        model.Entity<EntitlementOperation>().HasIndex(x => x.OperationKey).IsUnique();
        model.Entity<EntitlementOperation>().HasOne<Entitlement>().WithMany().HasForeignKey(x => x.EntitlementId);
        model.Entity<MockCredit>().HasIndex(x => new { x.OrderId, x.RequestKey }).IsUnique();
        model.Entity<MockCredit>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId);
        model.Entity<MockCredit>().HasOne<PaymentAttempt>().WithMany().HasForeignKey(x => x.AttemptId);

        model.Entity<DeploymentIdentity>().HasIndex(x => x.Key).IsUnique();
        model.Entity<Shopper>().HasIndex(x => new { x.Issuer, x.Subject }).IsUnique();
        model.Entity<ShopperSession>().HasIndex(x => x.TokenHash).IsUnique();
        model.Entity<ShopperSession>().HasIndex(x => x.ExpiresAt);
        model.Entity<Product>().HasIndex(x => x.Slug).IsUnique();
        model.Entity<Product>().HasIndex(x => new { x.Published, x.Id });
        model.Entity<ProductVariant>().HasIndex(x => x.Sku).IsUnique();
        model.Entity<Cart>().HasIndex(x => x.ShopperId).IsUnique();
        model.Entity<Cart>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<CartLine>().HasIndex(x => new { x.CartId, x.VariantId }).IsUnique();
        model.Entity<Quote>().HasIndex(x => new { x.ShopperId, x.ExpiresAt });
        model.Entity<Order>().HasIndex(x => x.Number).IsUnique();
        model.Entity<Order>().HasIndex(x => new { x.ShopperId, x.IdempotencyKey }).IsUnique();
        model.Entity<Order>().HasIndex(x => x.QuoteId).IsUnique();
        model.Entity<Order>().HasIndex(x => new { x.ShopperId, x.CreatedAt });
        model.Entity<Order>().HasIndex(x => new { x.PaymentState, x.ExpiresAt });
        model.Entity<StockReservation>().HasIndex(x => x.OrderLineId).IsUnique();
        model.Entity<StockReservation>().HasIndex(x => new { x.State, x.ExpiresAt });
        model.Entity<StockMovement>().HasIndex(x => x.EffectKey).IsUnique();
        model.Entity<PaymentAttempt>().HasIndex(x => new { x.MerchantId, x.QrId }).IsUnique();
        model.Entity<PaymentAttempt>().HasIndex(x => x.OrderId).IsUnique().HasFilter("\"State\" = 'AwaitingPayment'");
        model.Entity<BankTransaction>().HasIndex(x => new { x.Environment, x.MerchantId, x.BillerNo, x.InstructionId }).IsUnique();
        model.Entity<BankTransaction>().HasIndex(x => new { x.QrId, x.CreatedAt });
        model.Entity<BankTransaction>().HasIndex(x => new { x.Environment, x.MerchantId, x.PaymentOccurredAt });
        model.Entity<CallbackReceipt>().HasIndex(x => new { x.Environment, x.MerchantId, x.InstructionId }).IsUnique();
        model.Entity<PaymentObservation>().HasIndex(x => new { x.AttemptId, x.CreatedAt });
        model.Entity<PaymentAllocation>().HasIndex(x => x.BankTransactionId).IsUnique();
        model.Entity<PaymentAllocation>().HasIndex(x => x.OrderId).IsUnique().HasFilter("\"Primary\" = TRUE");
        model.Entity<VerificationRequest>().HasIndex(x => new { x.AttemptId, x.CreatedAt });
        model.Entity<ReconciliationCase>().HasIndex(x => x.EffectKey).IsUnique();
        model.Entity<ReconciliationCase>().HasIndex(x => new { x.State, x.CreatedAt });
        model.Entity<OutboxJob>().HasIndex(x => x.EffectKey).IsUnique();
        model.Entity<OutboxJob>().HasIndex(x => new { x.AvailableAt, x.Id }).HasFilter("\"CompletedAt\" IS NULL");
        model.Entity<AuditEvent>().HasIndex(x => new { x.Resource, x.CreatedAt });
        model.Entity<NumberSequence>().HasIndex(x => x.Scope).IsUnique();

        model.Entity<ShopperSession>().HasOne<Shopper>().WithMany().HasForeignKey(x => x.ShopperId);
        model.Entity<ProductVariant>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId);
        model.Entity<Cart>().HasOne<Shopper>().WithMany().HasForeignKey(x => x.ShopperId);
        model.Entity<CartLine>().HasOne<Cart>().WithMany().HasForeignKey(x => x.CartId);
        model.Entity<CartLine>().HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.VariantId);
        model.Entity<Quote>().HasOne<Shopper>().WithMany().HasForeignKey(x => x.ShopperId);
        model.Entity<Quote>().HasOne<Cart>().WithMany().HasForeignKey(x => x.CartId);
        model.Entity<Order>().HasOne<Shopper>().WithMany().HasForeignKey(x => x.ShopperId);
        model.Entity<Order>().HasOne<Quote>().WithMany().HasForeignKey(x => x.QuoteId);
        model.Entity<OrderLine>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId);
        model.Entity<OrderLine>().HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.VariantId);
        model.Entity<StockReservation>().HasOne<OrderLine>().WithMany().HasForeignKey(x => x.OrderLineId);
        model.Entity<StockReservation>().HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.VariantId);
        model.Entity<StockMovement>().HasOne<OrderLine>().WithMany().HasForeignKey(x => x.OrderLineId);
        model.Entity<StockMovement>().HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.VariantId);
        model.Entity<PaymentAttempt>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId);
        model.Entity<PaymentObservation>().HasOne<BankTransaction>().WithMany().HasForeignKey(x => x.BankTransactionId);
        model.Entity<PaymentObservation>().HasOne<PaymentAttempt>().WithMany().HasForeignKey(x => x.AttemptId);
        model.Entity<CallbackReceipt>().HasOne<PaymentObservation>().WithMany().HasForeignKey(x => x.ObservationId);
        model.Entity<PaymentAllocation>().HasOne<BankTransaction>().WithMany().HasForeignKey(x => x.BankTransactionId);
        model.Entity<PaymentAllocation>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId);
        model.Entity<VerificationRequest>().HasOne<PaymentAttempt>().WithMany().HasForeignKey(x => x.AttemptId);
        model.Entity<ReconciliationCase>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId);
        model.Entity<ReconciliationCase>().HasOne<BankTransaction>().WithMany().HasForeignKey(x => x.BankTransactionId);

        foreach (var entity in model.Model.GetEntityTypes())
        {
            entity.FindProperty(nameof(Entity.Id))!.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType.IsEnum) property.SetProviderClrType(typeof(string));
                if (property.ClrType == typeof(string))
                {
                    if (entity.ClrType == typeof(TestIssuerProfile)) { property.SetMaxLength(property.GetMaxLength() ?? 512); continue; }
                    if (property.Name.EndsWith("Json", StringComparison.Ordinal)) property.SetColumnType("jsonb");
                    else property.SetMaxLength(property.Name switch
                    {
                        nameof(Product.Description) => 10000,
                        nameof(FinanceCaseNote.Body) => 2000,
                        nameof(PaymentAttempt.Payload) => 1024,
                        nameof(OutboxJob.LastError) => 2000,
                        nameof(ShopperSession.TokenHash) or nameof(ShopperSession.CsrfHash) or nameof(PaymentObservation.PayloadHash) or nameof(Quote.SnapshotHash) or nameof(Order.RequestHash) => 64,
                        nameof(BankTransaction.InstructionId) => 50,
                        nameof(PaymentAttempt.QrId) => 20,
                        nameof(PaymentAttempt.BillerNo) => 15,
                        nameof(PaymentAttempt.Ref1) or nameof(PaymentAttempt.Ref2) => 20,
                        nameof(BankTransaction.BankTransDate) => 14,
                        _ => 512
                    });
                }
            }
            foreach (var foreignKey in entity.GetForeignKeys()) foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }
}

public sealed class CommerceDesignTimeFactory : IDesignTimeDbContextFactory<CommerceDb>
{
    public CommerceDb CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<CommerceDb>()
        .UseNpgsql(Environment.GetEnvironmentVariable("TTB_MIGRATION_CONNECTION") ?? "Host=localhost;Database=ttb_design_only;Username=ttb_design_only")
        .Options);
}
