using System.Text.Json;
using System.Text.Json.Nodes;
using TtbCommerce.Core;
using TtbCommerce.Infrastructure;
using Xunit;

namespace TtbCommerce.Tests;

public sealed class BankReadTests
{
    internal const string Biller = "111111111111111";
    internal const string Qr = "ZTSTE261018120000001";
    internal static JsonObject Response(string code = "0000") => new()
    {
        ["ResCode"] = code, ["ResDesc"] = "Successful", ["BillerNo"] = Biller, ["QRId"] = Qr, ["TransDate"] = "20261018115959",
        ["InstructionId"] = "UNIT-INSTRUCTION", ["Ref1"] = "REF1", ["Ref2"] = "REF2", ["Amount"] = "111.75", ["ResultCode"] = "0000", ["ResultDesc"] = "Successful"
    };
    internal static BankReply Reply(JsonNode value) => new(200, JsonSerializer.SerializeToUtf8Bytes(value));

    [Fact]
    public void Verify_requires_both_service_and_payment_success_and_preserves_optional_Ref2()
    {
        var success = Response(); success.Remove("Ref2");
        var result = BankReadResult.Parse(BankOperation.Verify, Reply(success), Biller, Qr);
        Assert.Equal("Found", result.Outcome); Assert.Null(Assert.Single(result.Credits).Ref2); Assert.Equal(11175, result.Credits[0].AmountMinor);
        success["ResultCode"] = "9999";
        Assert.Equal("Unconfirmed", BankReadResult.Parse(BankOperation.Verify, Reply(success), Biller, Qr).Outcome);
    }

    [Theory]
    [InlineData("0002", "NotFound")]
    [InlineData("0001", "Unconfirmed")]
    [InlineData("0003", "Unconfirmed")]
    [InlineData("9001", "AlreadyVoided")]
    [InlineData("9999", "Unconfirmed")]
    public void Verify_result_codes_follow_the_current_table_not_the_removed_example(string code, string outcome) =>
        Assert.Equal(outcome, BankReadResult.Parse(BankOperation.Verify, Reply(Response(code)), Biller, Qr).Outcome);

    [Fact]
    public void Unexpected_response_identity_and_transport_errors_never_confirm_payment()
    {
        var response = Response(); response["QRId"] = "ANOTHER-QR";
        Assert.Equal("ResponseIdentityMismatch", BankReadResult.Parse(BankOperation.Verify, Reply(response), Biller, Qr).Detail);
        Assert.Equal("Unconfirmed", BankReadResult.Parse(BankOperation.Verify, new(null, [], "Timeout"), Biller, Qr).Outcome);
        Assert.Equal("Unconfirmed", BankReadResult.Parse(BankOperation.Verify, new(401, []), Biller, Qr).Outcome);
    }

    [Fact]
    public void Range_supports_multiple_static_QR_transactions_and_rejects_mixed_QR_ids()
    {
        const string staticQr = "STSTE261018120000001";
        var first = Response(); first["QRId"] = staticQr; first["ResultCode"] = "000";
        var second = first.DeepClone().AsObject(); second["InstructionId"] = "SECOND-INSTRUCTION";
        var root = new JsonObject { ["ResCode"] = "0000", ["BillerNo"] = Biller, ["QRId"] = staticQr, ["TransDate"] = "20261018120000", ["QTransaction"] = new JsonArray(first, second) };
        Assert.Equal(2, BankReadResult.Parse(BankOperation.EnquiryRange, Reply(root), Biller, staticQr).Credits.Length);
        second["QRId"] = Qr;
        Assert.Equal("Unconfirmed", BankReadResult.Parse(BankOperation.EnquiryRange, Reply(root), Biller, staticQr).Outcome);
        root["ResCode"] = "0001"; root.Remove("QTransaction");
        Assert.Equal("NotFound", BankReadResult.Parse(BankOperation.EnquiryRange, Reply(root), Biller, staticQr).Outcome);
    }

    [Fact]
    public void Range_cannot_exceed_the_previous24hours_or_query_the_future()
    {
        var now = DateTimeOffset.UtcNow;
        TtbBankGateway.ValidateRange(now.AddHours(-24), now, now);
        Assert.Throws<CommerceException>(() => TtbBankGateway.ValidateRange(now.AddHours(-24).AddSeconds(-1), now, now));
        Assert.Throws<CommerceException>(() => TtbBankGateway.ValidateRange(now.AddHours(-1), now.AddSeconds(1), now));
    }
}
