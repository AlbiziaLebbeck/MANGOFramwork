namespace TtbCommerce.Core;

public sealed class CommerceException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
