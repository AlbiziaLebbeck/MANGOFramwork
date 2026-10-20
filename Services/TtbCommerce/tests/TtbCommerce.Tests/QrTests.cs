using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class QrTests
{
    private static ThaiQrInput BankFixture() => new("010753700001716", "ABCD1234567890XXXXXX", "2222222222", "ZXXXE231231235900001", new Money(19978), "ABC");

    [Fact]
    public void Bank_golden_payload_and_CRC_match_the_document_exactly()
    {
        const string expected = "00020101021230770016A00000067701011201150107537000017160220ABCD1234567890XXXXXX0310222222222253037645406199.785802TH5903ABC62240720ZXXXE231231235900001630463CF";
        Assert.Equal(expected, ThaiQr.Build(BankFixture()));
        Assert.Equal(0x29B1, ThaiQr.Crc("123456789"));
    }

    [Theory]
    [InlineData("lowercase")]
    [InlineData("REF 1")]
    [InlineData("REF-1")]
    [InlineData("REF_1")]
    [InlineData("123456789012345678901")]
    [InlineData("อ้างอิง")]
    public void QR_references_are_uppercase_ASCII_alphanumeric(string reference) => Assert.Throws<ArgumentException>(() => ThaiQr.Build(BankFixture() with { Ref1 = reference }));

    [Fact]
    public void QR_requires_correct_biller_amount_and_static_dynamic_profile()
    {
        Assert.Throws<ArgumentException>(() => ThaiQr.Build(BankFixture() with { BillerNo = "07555590000210" }));
        Assert.Throws<ArgumentException>(() => ThaiQr.Build(BankFixture() with { Amount = new Money(0) }));
        Assert.Throws<ArgumentException>(() => ThaiQr.Build(BankFixture() with { Amount = new Money(1000000000000) }));
        Assert.Throws<ArgumentException>(() => ThaiQr.Build(BankFixture() with { QrId = "SXXXE231231235900001" }));
        Assert.Contains("010211", ThaiQr.Build(BankFixture() with { QrId = "SXXXE231231235900001", Dynamic = false }));
    }

    [Fact]
    public void JWT_signature_is_RS256_and_claim_times_are_epoch_seconds()
    {
        using var privateKey = RSA.Create(2048);
        using var publicKey = RSA.Create(); publicKey.ImportFromPem(privateKey.ExportSubjectPublicKeyInfoPem());
        var now = DateTimeOffset.Parse("2026-10-18T00:00:00Z", CultureInfo.InvariantCulture);
        var token = BankJwtSigner.Create(privateKey, "unit-test-issuer", now, 120);
        var parts = token.Split('.'); Assert.Equal(3, parts.Length);
        static byte[] Decode(string text) => Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/').PadRight((text.Length + 3) / 4 * 4, '='));
        using var header = JsonDocument.Parse(Decode(parts[0])); using var payload = JsonDocument.Parse(Decode(parts[1]));
        Assert.Equal("RS256", header.RootElement.GetProperty("alg").GetString());
        Assert.Equal("JWT", header.RootElement.GetProperty("type").GetString());
        Assert.False(header.RootElement.TryGetProperty("typ", out _));
        Assert.Equal("unit-test-issuer", payload.RootElement.GetProperty("iss").GetString());
        Assert.Equal(now.ToUnixTimeSeconds(), payload.RootElement.GetProperty("iat").GetInt64());
        Assert.Equal(now.ToUnixTimeSeconds() + 120, payload.RootElement.GetProperty("exp").GetInt64());
        Assert.True(publicKey.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        Assert.False(publicKey.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}changed"), Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        Assert.Throws<ArgumentException>(() => BankJwtSigner.Create(privateKey, "unit-test-issuer", now, 1201));
    }

    [PgFact]
    public async Task Concurrent_QR_requests_return_one_persisted_attempt_and_mock_is_not_a_payment_payload()
    {
        await using var database = await PgDatabase.CreateAsync();
        Guid shopper; Guid orderId;
        await using (var db = database.Open())
        {
            var actor = await CartTests.ShopperAsync(db, "alice"); shopper = actor.ShopperId;
            var variant = (await CatalogTests.PublishedAsync(db)).Variant;
            var quote = await OrderTests.QuoteAsync(db, actor, (variant.Id, 1));
            orderId = (await OrderTests.Service(db).CreateAsync(shopper, new(quote.Id), "qr-test", default)).Id;
        }
        var values = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var db = database.Open();
            var options = new CommerceOptions(); var ttb = new TtbOptions();
            return await new QrService(db, new Numbering(db, TimeProvider.System), options, ttb, new(options, ttb), TimeProvider.System).GetOrCreateAsync(shopper, orderId, default);
        }));
        Assert.Single(values.Select(x => x.AttemptId).Distinct()); Assert.Single(values.Select(x => x.Payload).Distinct());
        Assert.StartsWith("MANGO-MOCK:", values[0].Payload); Assert.DoesNotContain("000201", values[0].Payload);
        Assert.Equal("Mock", values[0].Mode); Assert.Equal(20, values[0].QrId.Length);
        Assert.StartsWith("data:image/png;base64,iVBORw0KGgo", values[0].ImageDataUrl);
        var expiry = TimeZoneInfo.ConvertTime(values[0].ExpiresAt, Numbering.Bangkok);
        Assert.Equal(expiry.ToString("yyMMddHHmm", CultureInfo.InvariantCulture), values[0].QrId.Substring(5, 10));
        await using (var db = database.Open()) Assert.Equal(1, await db.Attempts.CountAsync());
    }

    [PgFact]
    public async Task Concurrent_distinct_orders_exhaust_the_last_QR_numbers_without_wrapping_or_leaving_partial_attempts()
    {
        await using var database = await PgDatabase.CreateAsync();
        var clock = new MutableClock(DateTimeOffset.Parse("2026-10-19T08:00:00+07:00", CultureInfo.InvariantCulture));
        var expires = clock.GetUtcNow().AddMinutes(5);
        var scope = $"qr:SIM:{TimeZoneInfo.ConvertTime(expires, Numbering.Bangkok):yyyyMMddHHmm}";
        Guid shopper; var orders = new List<Guid>();
        await using (var db = database.Open())
        {
            var actor = await CartTests.ShopperAsync(db, "qr-capacity"); shopper = actor.ShopperId;
            var variant = (await CatalogTests.PublishedAsync(db, stock: 10)).Variant;
            for (var i = 0; i < 4; i++)
            {
                var quote = await OrderTests.QuoteAsync(db, actor, (variant.Id, 1));
                var order = await OrderTests.Service(db).CreateAsync(shopper, new(quote.Id), $"capacity-{i}", default);
                orders.Add(order.Id);
                (await db.Orders.SingleAsync(x => x.Id == order.Id)).ExpiresAt = expires;
            }
            db.Sequences.Add(new NumberSequence { Scope = scope, Value = 99996 });
            await db.SaveChangesAsync();
        }
        var results = await Task.WhenAll(orders.Select(async orderId =>
        {
            await using var db = database.Open(); var commerce = new CommerceOptions(); var bank = new TtbOptions();
            try
            {
                var result = await new QrService(db, new(db, clock), commerce, bank, new(commerce, bank), clock).GetOrCreateAsync(shopper, orderId, default);
                return (Id: result.QrId, Error: (string?)null, OrderId: orderId);
            }
            catch (CommerceException error) { return (Id: (string?)null, Error: error.Code, OrderId: orderId); }
        }));
        Assert.Equal(3, results.Count(x => x.Id is not null));
        Assert.Equal(new[] { "99997", "99998", "99999" }, results.Where(x => x.Id is not null).Select(x => x.Id![^5..]).Order().ToArray());
        var rejected = Assert.Single(results, x => x.Error is not null); Assert.Equal("numbering_exhausted", rejected.Error);
        await using (var db = database.Open())
        {
            Assert.Equal(99999, (await db.Sequences.SingleAsync(x => x.Scope == scope)).Value);
            Assert.Equal(3, await db.Attempts.CountAsync());
            Assert.False(await db.Attempts.AnyAsync(x => x.OrderId == rejected.OrderId));
            var commerce = new CommerceOptions(); var bank = new TtbOptions();
            Assert.Equal("numbering_exhausted", (await Assert.ThrowsAsync<CommerceException>(() =>
                new QrService(db, new(db, clock), commerce, bank, new(commerce, bank), clock).GetOrCreateAsync(shopper, rejected.OrderId, default))).Code);
        }
    }

    [PgTheory]
    [InlineData("2026-10-15T23:59:59+07:00", "2026-10-16T00:02:59+07:00", "2610160002")]
    [InlineData("2026-10-31T23:59:01+07:00", "2026-11-01T00:02:01+07:00", "2611010002")]
    [InlineData("2026-12-31T23:59:59+07:00", "2027-01-01T00:02:01+07:00", "2701010002")]
    [InlineData("2028-02-28T23:59:59+07:00", "2028-02-29T00:02:59+07:00", "2802290002")]
    public async Task QR_expiry_uses_the_same_Bangkok_minute_across_calendar_boundaries_and_never_reissues_after_expiry(string now, string until, string expectedMinute)
    {
        await using var database = await PgDatabase.CreateAsync();
        var clock = new MutableClock(DateTimeOffset.Parse(now, CultureInfo.InvariantCulture));
        var reservationUntil = DateTimeOffset.Parse(until, CultureInfo.InvariantCulture).ToUniversalTime();
        await using var db = database.Open();
        var actor = await CartTests.ShopperAsync(db, "qr-boundary"); var variant = (await CatalogTests.PublishedAsync(db)).Variant;
        var quote = await OrderTests.QuoteAsync(db, actor, (variant.Id, 1));
        var order = await OrderTests.Service(db).CreateAsync(actor.ShopperId, new(quote.Id), "calendar", default);
        (await db.Orders.SingleAsync(x => x.Id == order.Id)).ExpiresAt = reservationUntil;
        await db.SaveChangesAsync();
        var commerce = new CommerceOptions(); var bank = new TtbOptions();
        var service = new QrService(db, new(db, clock), commerce, bank, new(commerce, bank), clock);
        var qr = await service.GetOrCreateAsync(actor.ShopperId, order.Id, default);
        Assert.Equal(expectedMinute, qr.QrId.Substring(5, 10));
        Assert.Equal(0, qr.ExpiresAt.Second); Assert.True(qr.ExpiresAt <= reservationUntil);
        Assert.True(reservationUntil - qr.ExpiresAt < TimeSpan.FromMinutes(1));
        Assert.Equal(qr.AttemptId, (await service.GetOrCreateAsync(actor.ShopperId, order.Id, default)).AttemptId);
        clock.Advance(qr.ExpiresAt - clock.GetUtcNow());
        Assert.Equal("order_expiring", (await Assert.ThrowsAsync<CommerceException>(() => service.GetOrCreateAsync(actor.ShopperId, order.Id, default))).Code);
        Assert.Equal(1, await db.Attempts.CountAsync());
    }

    [PgFact]
    public async Task A_window_with_only_sixty_seconds_after_minute_rounding_cannot_allocate_a_QR_or_sequence()
    {
        await using var database = await PgDatabase.CreateAsync(); await using var db = database.Open();
        var clock = new MutableClock(DateTimeOffset.Parse("2026-10-19T08:00:00+07:00", CultureInfo.InvariantCulture));
        var actor = await CartTests.ShopperAsync(db, "qr-too-short"); var variant = (await CatalogTests.PublishedAsync(db)).Variant;
        var quote = await OrderTests.QuoteAsync(db, actor, (variant.Id, 1));
        var order = await OrderTests.Service(db).CreateAsync(actor.ShopperId, new(quote.Id), "short", default);
        (await db.Orders.SingleAsync(x => x.Id == order.Id)).ExpiresAt = clock.GetUtcNow().AddSeconds(119);
        await db.SaveChangesAsync();
        var commerce = new CommerceOptions(); var bank = new TtbOptions();
        Assert.Equal("order_expiring", (await Assert.ThrowsAsync<CommerceException>(() =>
            new QrService(db, new(db, clock), commerce, bank, new(commerce, bank), clock).GetOrCreateAsync(actor.ShopperId, order.Id, default))).Code);
        Assert.Empty(await db.Attempts.ToListAsync()); Assert.False(await db.Sequences.AnyAsync(x => x.Scope.StartsWith("qr:")));
    }

    [PgFact]
    public async Task Incomplete_bank_setup_cannot_create_real_orders_or_QR()
    {
        await using var database = await PgDatabase.CreateAsync();
        Guid shopper; Guid orderId; Guid quoteId;
        await using (var db = database.Open())
        {
            var actor = await CartTests.ShopperAsync(db, "alice"); shopper = actor.ShopperId;
            var variant = (await CatalogTests.PublishedAsync(db)).Variant;
            quoteId = (await OrderTests.QuoteAsync(db, actor, (variant.Id, 1))).Id;
        }
        var bankOptions = new CommerceOptions { Environment = CommerceEnvironment.Uat, PaymentMode = PaymentMode.Bank };
        await using (var db = database.Open())
        {
            var error = await Assert.ThrowsAsync<CommerceException>(() => OrderTests.Service(db, bankOptions).CreateAsync(shopper, new(quoteId), "bank-not-ready", default));
            Assert.Equal("bank_unavailable", error.Code);
        }
        await using (var db = database.Open()) orderId = (await OrderTests.Service(db).CreateAsync(shopper, new(quoteId), "mock-only", default)).Id;
        await using (var db = database.Open())
        {
            var ttb = new TtbOptions { BillerNo = "075555900002101" };
            var qr = new QrService(db, new(db, TimeProvider.System), bankOptions, ttb, new(bankOptions, ttb), TimeProvider.System);
            Assert.Equal("bank_unavailable", (await Assert.ThrowsAsync<CommerceException>(() => qr.GetOrCreateAsync(shopper, orderId, default))).Code);
            Assert.Equal(0, await db.Attempts.CountAsync());
        }
    }
}
