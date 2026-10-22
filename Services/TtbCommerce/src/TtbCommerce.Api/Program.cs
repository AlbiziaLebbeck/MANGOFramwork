using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Api;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;

var operatorFlags = new[] { "--migrate", "--seed-demo", "--prepare-uat-test-catalog", "--uat-bank-probe", "--uat-bank-qr" };
if (args.Count(x => operatorFlags.Contains(x)) > 1)
    throw new InvalidOperationException("Operator commands must run separately.");
var builder = WebApplication.CreateBuilder(args.Where(x => !operatorFlags.Contains(x)).ToArray());
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 262144);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<CommerceErrors>();
builder.Services.AddSingleton(services =>
{
    var commerce = services.GetRequiredService<IConfiguration>().GetSection("Commerce").Get<CommerceOptions>() ?? new();
    var errors = commerce.Validate();
    if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
    if (commerce.Environment == CommerceEnvironment.Local && !services.GetRequiredService<IWebHostEnvironment>().IsDevelopment())
        throw new InvalidOperationException("Local commerce mode requires ASPNETCORE_ENVIRONMENT=Development.");
    return commerce;
});
builder.Services.AddSingleton(services => services.GetRequiredService<IConfiguration>().GetSection("Ttb").Get<TtbOptions>() ?? new());
builder.Services.AddSingleton<BankReadiness>();
builder.Services.AddSingleton<BankJwtSigner>();
builder.Services.AddSingleton(services => services.GetRequiredService<IConfiguration>().GetSection("Storage").Get<PrivateStorageOptions>() ?? new());
builder.Services.AddSingleton<PrivateFiles>();
builder.Services.AddSingleton(services => services.GetRequiredService<IConfiguration>().GetSection("Reports").Get<ReportOptions>() ?? new());
builder.Services.AddScoped<IReportSource, TtbReportSource>();
builder.Services.AddScoped<IReportDecryptor, PgpReportDecryptor>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<ReportFetchService>();
builder.Services.AddScoped<ICommerceJobHandler, ReportStageHandler>();
builder.Services.AddScoped<ICommerceJobHandler, ReportRowHandler>();
builder.Services.AddScoped<ICommerceJobHandler, ReportComparisonHandler>();
builder.Services.AddHostedService<ReportWorker>();
builder.Services.AddSingleton(services => services.GetRequiredService<IConfiguration>().GetSection("Receipts").Get<ReceiptOptions>() ?? new());
builder.Services.AddScoped<ReceiptService>();
builder.Services.AddScoped<IssuerProfileService>();
builder.Services.AddScoped<ICommerceJobHandler, ReceiptEnsureHandler>();
builder.Services.AddSingleton<IReceiptPdfRenderer, ChromiumReceiptRenderer>();
builder.Services.AddScoped<DocumentRenderHandler>();
builder.Services.AddHostedService<ReceiptWorker>();
builder.Services.AddDbContext<CommerceDb>((services, options) => options.UseNpgsql(services.GetRequiredService<IConfiguration>().GetConnectionString("Commerce")
    ?? throw new InvalidOperationException("ConnectionStrings:Commerce is required.")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(services => services.GetRequiredService<IConfiguration>().GetSection("BankResilience").Get<BankResilienceOptions>() ?? new());
builder.Services.AddSingleton<BankCircuitBreaker>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IIdentityVerifier, MangosIdentityVerifier>(client => client.Timeout = TimeSpan.FromSeconds(6))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<ShopperAccess>();
builder.Services.AddSingleton(services => services.GetRequiredService<IConfiguration>().GetSection("Dashboard").Get<PaymentDashboardOptions>() ?? new());
builder.Services.AddScoped<PaymentDashboardQueries>();
builder.Services.AddScoped<DurableWork>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<LocalDemoSeeder>();
builder.Services.AddScoped<UatTestCatalog>();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<Numbering>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<QrService>();
builder.Services.AddScoped<PaymentProcessor>();
builder.Services.AddSingleton(services => services.GetRequiredService<IConfiguration>().GetSection("BankTests").Get<BankTestOptions>() ?? new());
builder.Services.AddScoped<BankTestConsoleService>();
builder.Services.AddSingleton(services => services.GetRequiredService<IConfiguration>().GetSection("Voids").Get<VoidOptions>() ?? new());
builder.Services.AddScoped<VoidService>();
builder.Services.AddScoped<FinanceService>();
builder.Services.AddSingleton(services => services.GetRequiredService<IConfiguration>().GetSection("ManualRefunds").Get<ManualRefundOptions>() ?? new());
builder.Services.AddScoped<ManualRefundService>();
builder.Services.AddScoped<FinanceEvidenceService>();
builder.Services.AddScoped<GoodsDispositionService>();
builder.Services.AddSingleton(services => services.GetRequiredService<IConfiguration>().GetSection("Operations").Get<OperationsOptions>() ?? new());
builder.Services.AddSingleton<CallbackTelemetry>();
builder.Services.AddScoped<ICommerceJobHandler, VoidSendHandler>();
builder.Services.AddScoped<ICommerceJobHandler, VoidDocumentHandler>();
builder.Services.AddHttpClient<TtbBankGateway>(client => client.Timeout = TimeSpan.FromSeconds(10))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.AddScoped<MockBankGateway>();
builder.Services.AddScoped<MockBankService>();
builder.Services.AddScoped<IBankGateway>(services => services.GetRequiredService<CommerceOptions>().PaymentMode == PaymentMode.Mock
    ? services.GetRequiredService<MockBankGateway>() : services.GetRequiredService<TtbBankGateway>());
builder.Services.AddScoped<VerificationService>();
builder.Services.AddScoped<FulfillmentService>();
builder.Services.AddScoped<IOrderLineDelivery, PhysicalLineDelivery>();
builder.Services.AddScoped<IOrderLineDelivery, DigitalLineDelivery>();
builder.Services.AddScoped<DigitalAssetService>();
builder.Services.AddScoped<EntitlementService>();
builder.Services.AddScoped<GameTicketService>();
builder.Services.AddScoped<ICommerceJobHandler, FulfillmentDispatchHandler>();
builder.Services.AddCallbackTransport();
builder.Services.AddScoped<ICommerceJobHandler, OrderExpiryHandler>();
builder.Services.AddHostedService<CommerceWorker>();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase, allowIntegerValues: false)));
builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>().Configure<CommerceOptions>((options, commerce) => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(commerce.TrustedOrigins).WithMethods("GET", "POST", "PUT", "DELETE", "PATCH").AllowAnyHeader().WithExposedHeaders("Content-Disposition").AllowCredentials()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("dashboard", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
    {
        PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
    }));
    options.AddPolicy("session", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
    {
        PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
    }));
});

var app = builder.Build();
var commerce = app.Services.GetRequiredService<CommerceOptions>();
_ = app.Services.GetRequiredService<BankCircuitBreaker>();
app.Services.GetRequiredService<ReceiptOptions>().AssertProductionReady(commerce.Environment);
await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<CommerceDb>();
    var migrate = args.Contains("--migrate", StringComparer.Ordinal);
    var seedDemo = args.Contains("--seed-demo", StringComparer.Ordinal);
    var prepareUatCatalog = args.Contains("--prepare-uat-test-catalog", StringComparer.Ordinal);
    if (migrate)
    {
        await DbGuard.AssertExistingEnvironmentAsync(database, commerce.Environment);
        await database.Database.MigrateAsync();
        await DbGuard.AssertEnvironmentAsync(database, commerce.Environment);
    }
    else if (!prepareUatCatalog) await DbGuard.AssertEnvironmentAsync(database, commerce.Environment);
    if (seedDemo) await scope.ServiceProvider.GetRequiredService<LocalDemoSeeder>().SeedAsync();
    if (args.Any(x => x is "--uat-bank-probe" or "--uat-bank-qr"))
    {
        if (scope.ServiceProvider.GetRequiredService<OperationsOptions>().RecoveryFreeze)
            throw new InvalidOperationException("Bank diagnostics cannot run during recovery freeze.");
        var result = await UatBankDiagnostics.RunAsync(database, scope.ServiceProvider.GetRequiredService<Numbering>(), commerce,
            scope.ServiceProvider.GetRequiredService<TtbOptions>(), scope.ServiceProvider.GetRequiredService<BankReadiness>(),
            scope.ServiceProvider.GetRequiredService<IBankGateway>(), scope.ServiceProvider.GetRequiredService<TimeProvider>(), args.Contains("--uat-bank-probe"), default);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
        return;
    }
    if (prepareUatCatalog)
    {
        if (scope.ServiceProvider.GetRequiredService<BankReadiness>().MissingForCheckout().Count == 0)
            throw new InvalidOperationException("Initial UAT test catalog preparation must run while bank payments are closed.");
        var prepared = await scope.ServiceProvider.GetRequiredService<UatTestCatalog>().PrepareAsync();
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { status = "uat-test-catalog-prepared", prepared.AlreadyPrepared, prepared.ProductIds,
            prepared.PhysicalProducts, prepared.DigitalProducts, financialRecordsCreated = false, bankConfigurationChanged = false, simulatorEnabled = false }));
    }
    if (migrate || seedDemo || prepareUatCatalog) return;
}
app.UseExceptionHandler();
app.UseCallbackTelemetry();
app.UseUatTransportProbe();
app.UseBankTestConsoleSocket();
app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    await next(context);
});
app.UseCors();
app.Use(async (context, next) =>
{
    if (context.RequestServices.GetRequiredService<OperationsOptions>().RecoveryFreeze && context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
    {
        context.Response.StatusCode = 503;
        await context.Response.WriteAsJsonAsync(new { code = "recovery_frozen", title = "Recovery inspection is read-only. Reconcile bank evidence before resuming mutations or workers." });
        return;
    }
    await next(context);
});
app.UseRateLimiter();
app.MapGet("/health/live", () => Results.Ok(new { status = "alive", service = "ttb-commerce" }));
app.MapGet("/health/ready", async (CommerceDb database, BankReadiness bank, ReportOptions reports, OperationsOptions operations, CancellationToken cancellationToken) =>
{
    var databaseReady = await database.Database.CanConnectAsync(cancellationToken);
    var bankReady = !operations.RecoveryFreeze && bank.MissingForCheckout().Count == 0;
    // The catalog/account service can be ready while bank provisioning is pending. Checkout separately checks bank availability.
    return Results.Json(new { status = databaseReady ? "ready" : "unavailable", database = databaseReady, paymentsReady = bankReady,
        bankAcceptanceComplete = bank.Missing().Count == 0, paymentMode = commerce.PaymentMode.ToString(),
        recoveryFrozen = operations.RecoveryFreeze, reportsEnabled = reports.Enabled, reportsReady = !operations.RecoveryFreeze && commerce.Environment != CommerceEnvironment.Local && reports.Enabled && reports.Missing().Count == 0 && File.Exists(reports.PgpPrivateKeyPath) }, statusCode: databaseReady ? 200 : 503);
});
app.MapGroup(commerce.PathPrefix).MapSessionEndpoints();
app.MapGroup(commerce.PathPrefix).MapCatalogEndpoints();
app.MapGroup(commerce.PathPrefix).MapCartEndpoints();
app.MapGroup(commerce.PathPrefix).MapOrderEndpoints();
app.MapGroup(commerce.PathPrefix).MapFulfillmentEndpoints();
app.MapGroup(commerce.PathPrefix).MapDigitalEndpoints();
app.MapGroup(commerce.PathPrefix).MapGameEndpoints();
app.MapGroup(commerce.PathPrefix).MapDocumentEndpoints();
app.MapGroup(commerce.PathPrefix).MapReportEndpoints();
app.MapGroup(commerce.PathPrefix).MapVoidEndpoints();
app.MapGroup(commerce.PathPrefix).MapFinanceEndpoints();
app.MapGroup(commerce.PathPrefix).MapManualRefundEndpoints();
app.MapGet(commerce.PathPrefix + "/storefront/config", (BankReadiness bank, TtbOptions ttb, PrivateStorageOptions storage, OperationsOptions operations) => Results.Ok(new
{
    environment = commerce.Environment.ToString(), paymentMode = commerce.PaymentMode.ToString(), paymentsReady = !operations.RecoveryFreeze && bank.MissingForCheckout().Count == 0,
    bankAcceptanceComplete = bank.Missing().Count == 0, callbackValidated = ttb.CallbackSourceIpValidated,
    uatTestCheckout = commerce.Environment == CommerceEnvironment.Uat && ttb.UatTestCheckoutEnabled,
    currency = "THB", verificationDelaySeconds = commerce.VerificationDelaySeconds, verificationBudget = commerce.VerificationBudget,
    mockSimulation = commerce.Environment == CommerceEnvironment.Local && commerce.PaymentMode == PaymentMode.Mock,
    gameUseReady = storage.GameServerKey.Length >= 32 && !string.IsNullOrEmpty(storage.GameServerId)
}));
app.MapCallbackEndpoints();
app.MapBankTestConsole();
app.MapPaymentDashboard();
app.MapOperationsEndpoints();
app.Run();

public partial class Program;
