using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TtbCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCommerce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ttb");

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Actor = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Action = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Resource = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DataJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BankTransactions",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<string>(type: "text", nullable: false),
                    MerchantId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    InstructionId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BillerNo = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    QrId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Ref1 = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Ref2 = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    BankTransDate = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    PaymentOccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PaymentTimeSource = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ResultCode = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CrossBorderFlag = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SwitchCode = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    PayerJson = table.Column<string>(type: "jsonb", nullable: true),
                    Voided = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransactions", x => x.Id);
                    table.CheckConstraint("CK_BankTransactions_Amount", "\"AmountMinor\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "Deployments",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Environment = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deployments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Jobs",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Kind = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Published = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    PreviewJson = table.Column<string>(type: "jsonb", nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TaxCode = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sequences",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sequences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Shoppers",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Issuer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Subject = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shoppers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Variants",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    PriceMinor = table.Column<long>(type: "bigint", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    TrackStock = table.Column<bool>(type: "boolean", nullable: false),
                    OnHand = table.Column<int>(type: "integer", nullable: false),
                    Reserved = table.Column<int>(type: "integer", nullable: false),
                    OptionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    DigitalAssetKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Variants", x => x.Id);
                    table.CheckConstraint("CK_Variants_Price", "\"PriceMinor\" >= 0");
                    table.CheckConstraint("CK_Variants_Stock", "\"OnHand\" >= 0 AND \"Reserved\" >= 0 AND \"Reserved\" <= \"OnHand\"");
                    table.ForeignKey(
                        name: "FK_Variants_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "ttb",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Carts",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopperId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Carts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Carts_Shoppers_ShopperId",
                        column: x => x.ShopperId,
                        principalSchema: "ttb",
                        principalTable: "Shoppers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopperId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CsrfHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sessions_Shoppers_ShopperId",
                        column: x => x.ShopperId,
                        principalSchema: "ttb",
                        principalTable: "Shoppers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CartLines",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CartLines", x => x.Id);
                    table.CheckConstraint("CK_CartLines_Quantity", "\"Quantity\" BETWEEN 1 AND 99");
                    table.ForeignKey(
                        name: "FK_CartLines_Carts_CartId",
                        column: x => x.CartId,
                        principalSchema: "ttb",
                        principalTable: "Carts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CartLines_Variants_VariantId",
                        column: x => x.VariantId,
                        principalSchema: "ttb",
                        principalTable: "Variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Quotes",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopperId = table.Column<Guid>(type: "uuid", nullable: false),
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    CartVersion = table.Column<long>(type: "bigint", nullable: false),
                    SubtotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    ShippingMinor = table.Column<long>(type: "bigint", nullable: false),
                    TotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    SnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedByOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotes", x => x.Id);
                    table.CheckConstraint("CK_Quotes_Total", "\"SubtotalMinor\" >= 0 AND \"ShippingMinor\" >= 0 AND \"TotalMinor\" = \"SubtotalMinor\" + \"ShippingMinor\"");
                    table.ForeignKey(
                        name: "FK_Quotes_Carts_CartId",
                        column: x => x.CartId,
                        principalSchema: "ttb",
                        principalTable: "Carts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Quotes_Shoppers_ShopperId",
                        column: x => x.ShopperId,
                        principalSchema: "ttb",
                        principalTable: "Shoppers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopperId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    PaymentState = table.Column<string>(type: "text", nullable: false),
                    Currency = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SubtotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    ShippingMinor = table.Column<long>(type: "bigint", nullable: false),
                    TotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    BuyerSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.CheckConstraint("CK_Orders_Total", "\"SubtotalMinor\" >= 0 AND \"ShippingMinor\" >= 0 AND \"TotalMinor\" > 0 AND \"TotalMinor\" = \"SubtotalMinor\" + \"ShippingMinor\"");
                    table.ForeignKey(
                        name: "FK_Orders_Quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalSchema: "ttb",
                        principalTable: "Quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_Shoppers_ShopperId",
                        column: x => x.ShopperId,
                        principalSchema: "ttb",
                        principalTable: "Shoppers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Allocations",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BankTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Primary = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Allocations", x => x.Id);
                    table.CheckConstraint("CK_Allocations_Amount", "\"AmountMinor\" > 0");
                    table.ForeignKey(
                        name: "FK_Allocations_BankTransactions_BankTransactionId",
                        column: x => x.BankTransactionId,
                        principalSchema: "ttb",
                        principalTable: "BankTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Allocations_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "ttb",
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Attempts",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    BillerNo = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    QrId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Ref1 = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Ref2 = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Payload = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Profile = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ExpectedMinor = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerificationCount = table.Column<int>(type: "integer", nullable: false),
                    LastVerificationAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerificationLeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attempts", x => x.Id);
                    table.CheckConstraint("CK_Attempts_Amount", "\"ExpectedMinor\" > 0 AND \"VerificationCount\" BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_Attempts_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "ttb",
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Cases",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    BankTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    EffectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Kind = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    State = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    ResolutionJson = table.Column<string>(type: "jsonb", nullable: true),
                    Assignee = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cases_BankTransactions_BankTransactionId",
                        column: x => x.BankTransactionId,
                        principalSchema: "ttb",
                        principalTable: "BankTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Cases_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "ttb",
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderLines",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Sku = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    VariantName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPriceMinor = table.Column<long>(type: "bigint", nullable: false),
                    TotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderLines", x => x.Id);
                    table.CheckConstraint("CK_OrderLines_Total", "\"Quantity\" BETWEEN 1 AND 99 AND \"UnitPriceMinor\" >= 0 AND \"TotalMinor\" = \"Quantity\"::bigint * \"UnitPriceMinor\"");
                    table.ForeignKey(
                        name: "FK_OrderLines_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "ttb",
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLines_Variants_VariantId",
                        column: x => x.VariantId,
                        principalSchema: "ttb",
                        principalTable: "Variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Observations",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BankTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Observations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Observations_Attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalSchema: "ttb",
                        principalTable: "Attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Observations_BankTransactions_BankTransactionId",
                        column: x => x.BankTransactionId,
                        principalSchema: "ttb",
                        principalTable: "BankTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VerificationRequests",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Service = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Actor = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RequestUid = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerificationRequests_Attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalSchema: "ttb",
                        principalTable: "Attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Reservations",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                    table.CheckConstraint("CK_Reservations_Quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_Reservations_OrderLines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalSchema: "ttb",
                        principalTable: "OrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reservations_Variants_VariantId",
                        column: x => x.VariantId,
                        principalSchema: "ttb",
                        principalTable: "Variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockMovements",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    EffectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    OnHandDelta = table.Column<int>(type: "integer", nullable: false),
                    ReservedDelta = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Actor = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockMovements_OrderLines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalSchema: "ttb",
                        principalTable: "OrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_Variants_VariantId",
                        column: x => x.VariantId,
                        principalSchema: "ttb",
                        principalTable: "Variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CallbackReceipts",
                schema: "ttb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<string>(type: "text", nullable: false),
                    MerchantId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    InstructionId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AckJson = table.Column<string>(type: "jsonb", nullable: false),
                    AckCode = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ObservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryCount = table.Column<int>(type: "integer", nullable: false),
                    LastReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallbackReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CallbackReceipts_Observations_ObservationId",
                        column: x => x.ObservationId,
                        principalSchema: "ttb",
                        principalTable: "Observations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_BankTransactionId",
                schema: "ttb",
                table: "Allocations",
                column: "BankTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_OrderId",
                schema: "ttb",
                table: "Allocations",
                column: "OrderId",
                unique: true,
                filter: "\"Primary\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_MerchantId_QrId",
                schema: "ttb",
                table: "Attempts",
                columns: new[] { "MerchantId", "QrId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_OrderId",
                schema: "ttb",
                table: "Attempts",
                column: "OrderId",
                unique: true,
                filter: "\"State\" = 'AwaitingPayment'");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Resource_CreatedAt",
                schema: "ttb",
                table: "AuditEvents",
                columns: new[] { "Resource", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_Environment_MerchantId_BillerNo_Instructio~",
                schema: "ttb",
                table: "BankTransactions",
                columns: new[] { "Environment", "MerchantId", "BillerNo", "InstructionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_QrId_CreatedAt",
                schema: "ttb",
                table: "BankTransactions",
                columns: new[] { "QrId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CallbackReceipts_Environment_MerchantId_InstructionId",
                schema: "ttb",
                table: "CallbackReceipts",
                columns: new[] { "Environment", "MerchantId", "InstructionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CallbackReceipts_ObservationId",
                schema: "ttb",
                table: "CallbackReceipts",
                column: "ObservationId");

            migrationBuilder.CreateIndex(
                name: "IX_CartLines_CartId_VariantId",
                schema: "ttb",
                table: "CartLines",
                columns: new[] { "CartId", "VariantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CartLines_VariantId",
                schema: "ttb",
                table: "CartLines",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_Carts_ShopperId",
                schema: "ttb",
                table: "Carts",
                column: "ShopperId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cases_BankTransactionId",
                schema: "ttb",
                table: "Cases",
                column: "BankTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_EffectKey",
                schema: "ttb",
                table: "Cases",
                column: "EffectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cases_OrderId",
                schema: "ttb",
                table: "Cases",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_State_CreatedAt",
                schema: "ttb",
                table: "Cases",
                columns: new[] { "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_Key",
                schema: "ttb",
                table: "Deployments",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_AvailableAt_Id",
                schema: "ttb",
                table: "Jobs",
                columns: new[] { "AvailableAt", "Id" },
                filter: "\"CompletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_EffectKey",
                schema: "ttb",
                table: "Jobs",
                column: "EffectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Observations_AttemptId_CreatedAt",
                schema: "ttb",
                table: "Observations",
                columns: new[] { "AttemptId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Observations_BankTransactionId",
                schema: "ttb",
                table: "Observations",
                column: "BankTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_OrderId",
                schema: "ttb",
                table: "OrderLines",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_VariantId",
                schema: "ttb",
                table: "OrderLines",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Number",
                schema: "ttb",
                table: "Orders",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PaymentState_ExpiresAt",
                schema: "ttb",
                table: "Orders",
                columns: new[] { "PaymentState", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_QuoteId",
                schema: "ttb",
                table: "Orders",
                column: "QuoteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ShopperId_CreatedAt",
                schema: "ttb",
                table: "Orders",
                columns: new[] { "ShopperId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ShopperId_IdempotencyKey",
                schema: "ttb",
                table: "Orders",
                columns: new[] { "ShopperId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_Published_Id",
                schema: "ttb",
                table: "Products",
                columns: new[] { "Published", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Slug",
                schema: "ttb",
                table: "Products",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_CartId",
                schema: "ttb",
                table: "Quotes",
                column: "CartId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_ShopperId_ExpiresAt",
                schema: "ttb",
                table: "Quotes",
                columns: new[] { "ShopperId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_OrderLineId",
                schema: "ttb",
                table: "Reservations",
                column: "OrderLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_State_ExpiresAt",
                schema: "ttb",
                table: "Reservations",
                columns: new[] { "State", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_VariantId",
                schema: "ttb",
                table: "Reservations",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_Sequences_Scope",
                schema: "ttb",
                table: "Sequences",
                column: "Scope",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_ExpiresAt",
                schema: "ttb",
                table: "Sessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_ShopperId",
                schema: "ttb",
                table: "Sessions",
                column: "ShopperId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_TokenHash",
                schema: "ttb",
                table: "Sessions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shoppers_Issuer_Subject",
                schema: "ttb",
                table: "Shoppers",
                columns: new[] { "Issuer", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_EffectKey",
                schema: "ttb",
                table: "StockMovements",
                column: "EffectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_OrderLineId",
                schema: "ttb",
                table: "StockMovements",
                column: "OrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_VariantId",
                schema: "ttb",
                table: "StockMovements",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_Variants_ProductId",
                schema: "ttb",
                table: "Variants",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Variants_Sku",
                schema: "ttb",
                table: "Variants",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerificationRequests_AttemptId_CreatedAt",
                schema: "ttb",
                table: "VerificationRequests",
                columns: new[] { "AttemptId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Allocations",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "AuditEvents",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "CallbackReceipts",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "CartLines",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "Cases",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "Deployments",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "Jobs",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "Reservations",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "Sequences",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "Sessions",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "StockMovements",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "VerificationRequests",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "Observations",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "OrderLines",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "Attempts",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "BankTransactions",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "Variants",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "Orders",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "Products",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "Quotes",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "Carts",
                schema: "ttb");

            migrationBuilder.DropTable(
                name: "Shoppers",
                schema: "ttb");
        }
    }
}
