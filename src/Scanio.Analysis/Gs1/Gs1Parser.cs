using System.Collections.Immutable;
using System.Globalization;

namespace Scanio.Analysis.Gs1;

public static class Gs1Parser
{
    private const char GroupSeparator = '\u001D';

    public static Gs1ParseResult Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var hasAimPrefix = text.StartsWith("]d2", StringComparison.Ordinal);
        var source = hasAimPrefix ? text[3..] : text;
        return source.StartsWith('(')
            ? ParseParenthesized(source, hasAimPrefix)
            : ParseRaw(source, hasAimPrefix);
    }

    private static Gs1ParseResult ParseParenthesized(string source, bool hasAimPrefix)
    {
        var elements = ImmutableArray.CreateBuilder<Gs1Element>();
        var errors = ImmutableArray.CreateBuilder<string>();
        var warnings = ImmutableArray.CreateBuilder<string>();
        var position = 0;

        while (position < source.Length)
        {
            if (source[position] != '(')
            {
                errors.Add($"Unexpected GS1 data at position {position}.");
                break;
            }

            var close = source.IndexOf(')', position + 1);
            if (close < 0)
            {
                errors.Add("GS1 application identifier is missing a closing parenthesis.");
                break;
            }

            var code = source[(position + 1)..close];
            var valueStart = close + 1;
            var next = source.IndexOf('(', valueStart);
            var valueEnd = next < 0 ? source.Length : next;
            var value = source[valueStart..valueEnd];

            if (!Gs1AiCatalog.TryResolve(code, out var identifier))
            {
                errors.Add($"Unsupported GS1 application identifier {code}.");
            }
            else
            {
                elements.Add(new(identifier, code, value));
                Validate(identifier, code, value, errors);
            }

            position = valueEnd;
        }

        return new(
            elements.Count > 0 || errors.Count > 0,
            true,
            elements.ToImmutable(),
            errors.ToImmutable(),
            warnings.ToImmutable());
    }

    private static Gs1ParseResult ParseRaw(string source, bool hasAimPrefix)
    {
        var elements = ImmutableArray.CreateBuilder<Gs1Element>();
        var errors = ImmutableArray.CreateBuilder<string>();
        var warnings = ImmutableArray.CreateBuilder<string>();
        var hasSeparator = source.Contains(GroupSeparator, StringComparison.Ordinal);
        var position = 0;

        while (position < source.Length)
        {
            if (source[position] == GroupSeparator)
            {
                position++;
                continue;
            }

            if (!Gs1AiCatalog.TryResolve(source.AsSpan(position), out var identifier, out var codeLength))
            {
                if (elements.Count > 0)
                {
                    errors.Add($"Unsupported or incomplete GS1 data at position {position}.");
                }

                break;
            }

            var code = source.Substring(position, codeLength);
            position += codeLength;
            string value;

            if (identifier.IsFixedLength)
            {
                var available = Math.Min(identifier.MaxLength, source.Length - position);
                value = source.Substring(position, available);
                position += available;
            }
            else
            {
                var separator = source.IndexOf(GroupSeparator, position);
                var valueEnd = separator < 0 ? source.Length : separator;
                value = source[position..valueEnd];
                position = separator < 0 ? source.Length : separator + 1;
                if (separator < 0)
                {
                    warnings.Add($"Variable-length AI {code} reaches the end of the payload; a missing GS separator may make following fields ambiguous.");
                }
            }

            elements.Add(new(identifier, code, value));
            Validate(identifier, code, value, errors);
        }

        return new(
            elements.Count > 0,
            hasAimPrefix || hasSeparator,
            elements.ToImmutable(),
            errors.ToImmutable(),
            warnings.ToImmutable());
    }

    private static void Validate(
        Gs1ApplicationIdentifier identifier,
        string code,
        string value,
        ImmutableArray<string>.Builder errors)
    {
        if (value.Length < identifier.MinLength || value.Length > identifier.MaxLength ||
            (identifier.IsFixedLength && value.Length != identifier.MaxLength))
        {
            var expected = identifier.IsFixedLength
                ? $"exactly {identifier.MaxLength}"
                : $"between {identifier.MinLength} and {identifier.MaxLength}";
            errors.Add($"AI {code} must contain {expected} characters.");
        }

        if (identifier.CharacterSet == Gs1CharacterSet.Numeric && value.Any(character => character is < '0' or > '9'))
        {
            errors.Add($"AI {code} must contain only numeric characters.");
        }
        else if (identifier.CharacterSet == Gs1CharacterSet.Gs1Text &&
                 value.Any(character => character < 0x20 || character > 0x7E))
        {
            errors.Add($"AI {code} contains characters outside the supported GS1 text set.");
        }

        if (identifier.HasCheckDigit && value.Length == identifier.MaxLength && value.All(char.IsAsciiDigit) &&
            CalculateCheckDigit(value.AsSpan(0, value.Length - 1)) != value[^1] - '0')
        {
            errors.Add($"AI {code} has an invalid GS1 check digit.");
        }

        if (identifier.IsDate && value.Length == 6 && value.All(char.IsAsciiDigit) && !IsValidDate(value))
        {
            errors.Add($"AI {code} is not a valid YYMMDD date.");
        }
    }

    private static int CalculateCheckDigit(ReadOnlySpan<char> data)
    {
        var sum = 0;
        var weight = 3;
        for (var index = data.Length - 1; index >= 0; index--)
        {
            sum += (data[index] - '0') * weight;
            weight = weight == 3 ? 1 : 3;
        }

        return (10 - sum % 10) % 10;
    }

    private static bool IsValidDate(string value)
    {
        var year = 2000 + int.Parse(value.AsSpan(0, 2), CultureInfo.InvariantCulture);
        var month = int.Parse(value.AsSpan(2, 2), CultureInfo.InvariantCulture);
        var day = int.Parse(value.AsSpan(4, 2), CultureInfo.InvariantCulture);

        if (month is < 1 or > 12)
        {
            return false;
        }

        // GS1 permits day 00 to represent the last day of the month.
        return day == 0 || day <= DateTime.DaysInMonth(year, month);
    }
}
