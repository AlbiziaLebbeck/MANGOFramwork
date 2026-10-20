using System.Globalization;

namespace TtbCommerce.Core;

/// <summary>THB amounts are exact integer satang. Identifiers and currencies never enter this parser.</summary>
public readonly record struct Money
{
    public long Minor { get; }

    public Money(long minor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        Minor = minor;
    }

    public static Money Parse(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 20)
            throw new FormatException("Amount must contain decimal digits with at most two fractional digits.");

        var dot = text.IndexOf('.');
        var whole = dot < 0 ? text.AsSpan() : text.AsSpan(0, dot);
        var fractional = dot < 0 ? ReadOnlySpan<char>.Empty : text.AsSpan(dot + 1);
        if (whole.IsEmpty || (dot >= 0 && fractional.Length is < 1 or > 2))
            throw new FormatException("Invalid THB precision.");
        foreach (var digit in whole)
            if (digit is < '0' or > '9') throw new FormatException("Invalid amount digit.");
        foreach (var digit in fractional)
            if (digit is < '0' or > '9') throw new FormatException("Invalid amount digit.");

        try
        {
            var units = long.Parse(whole, NumberStyles.None, CultureInfo.InvariantCulture);
            var cents = fractional.IsEmpty ? 0 : long.Parse(fractional, NumberStyles.None, CultureInfo.InvariantCulture);
            if (fractional.Length == 1) cents *= 10;
            return new Money(checked(units * 100 + cents));
        }
        catch (OverflowException exception)
        {
            throw new FormatException("Amount exceeds supported precision.", exception);
        }
    }

    public Money Add(Money other) => new(checked(Minor + other.Minor));
    public Money Multiply(int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);
        return new Money(checked(Minor * quantity));
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Minor / 100}.{Minor % 100:D2}");
}
