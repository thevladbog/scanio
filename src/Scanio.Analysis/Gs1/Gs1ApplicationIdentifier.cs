namespace Scanio.Analysis.Gs1;

public enum Gs1CharacterSet
{
    Numeric,
    Gs1Text
}

public sealed record Gs1ApplicationIdentifier(
    string Code,
    string Name,
    int MinLength,
    int MaxLength,
    bool IsFixedLength,
    Gs1CharacterSet CharacterSet,
    bool HasCheckDigit = false,
    bool IsDate = false);

public sealed record Gs1Element(
    Gs1ApplicationIdentifier Identifier,
    string Code,
    string Value);

public sealed record Gs1ParseResult(
    bool IsRecognized,
    bool HasExplicitSyntax,
    IReadOnlyList<Gs1Element> Elements,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
