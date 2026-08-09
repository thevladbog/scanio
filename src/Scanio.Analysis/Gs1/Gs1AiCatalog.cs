namespace Scanio.Analysis.Gs1;

public static class Gs1AiCatalog
{
    private static readonly IReadOnlyDictionary<string, Gs1ApplicationIdentifier> Exact =
        new Dictionary<string, Gs1ApplicationIdentifier>(StringComparer.Ordinal)
        {
            ["00"] = new("00", "SSCC", 18, 18, true, Gs1CharacterSet.Numeric, HasCheckDigit: true),
            ["01"] = new("01", "GTIN", 14, 14, true, Gs1CharacterSet.Numeric, HasCheckDigit: true),
            ["10"] = new("10", "Batch or lot", 1, 20, false, Gs1CharacterSet.Gs1Text),
            ["11"] = new("11", "Production date", 6, 6, true, Gs1CharacterSet.Numeric, IsDate: true),
            ["17"] = new("17", "Expiration date", 6, 6, true, Gs1CharacterSet.Numeric, IsDate: true),
            ["21"] = new("21", "Serial number", 1, 20, false, Gs1CharacterSet.Gs1Text),
            ["91"] = new("91", "Verification key", 1, 90, false, Gs1CharacterSet.Gs1Text),
            ["92"] = new("92", "Crypto tail", 1, 90, false, Gs1CharacterSet.Gs1Text)
        };

    public static bool TryResolve(ReadOnlySpan<char> input, out Gs1ApplicationIdentifier identifier, out int codeLength)
    {
        if (input.Length >= 4 && input[..4].ToString() is var familyCode && IsPriceFamily(familyCode))
        {
            var isCurrencyPrice = familyCode.StartsWith("393", StringComparison.Ordinal);
            identifier = new(
                familyCode,
                isCurrencyPrice ? "Price with ISO currency" : "Price",
                isCurrencyPrice ? 4 : 1,
                isCurrencyPrice ? 18 : 15,
                false,
                Gs1CharacterSet.Numeric);
            codeLength = 4;
            return true;
        }

        if (input.Length >= 2 && Exact.TryGetValue(input[..2].ToString(), out var exact))
        {
            identifier = exact;
            codeLength = 2;
            return true;
        }

        identifier = null!;
        codeLength = 0;
        return false;
    }

    public static bool TryResolve(string code, out Gs1ApplicationIdentifier identifier)
    {
        if (Exact.TryGetValue(code, out var exact))
        {
            identifier = exact;
            return true;
        }

        if (code.Length == 4 && IsPriceFamily(code))
        {
            var resolved = TryResolve(code.AsSpan(), out identifier, out var length);
            return resolved && length == code.Length;
        }

        identifier = null!;
        return false;
    }

    private static bool IsPriceFamily(string code) =>
        code.Length == 4 &&
        code[3] is >= '0' and <= '9' &&
        (code.StartsWith("392", StringComparison.Ordinal) || code.StartsWith("393", StringComparison.Ordinal));
}
