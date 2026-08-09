using System.Collections.Immutable;
using Scanio.Analysis.Gs1;

namespace Scanio.Analysis;

public static class HonestSignProductGroupClassifier
{
    public static ImmutableArray<string> Classify(IReadOnlyList<Gs1Element> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        var serial = elements.FirstOrDefault(element => element.Code == "21")?.Value;
        if (serial is null)
        {
            return ImmutableArray<string>.Empty;
        }

        if (serial.Length == 7 && Has(elements, "8005") && Has(elements, "93"))
        {
            return ImmutableArray.Create("Tobacco unit pack");
        }

        if (serial.Length == 13 && Has(elements, "91") && Has(elements, "92"))
        {
            return ImmutableArray.Create(
                "Footwear",
                "Light industry",
                "Perfumery",
                "Pharmaceuticals");
        }

        return ImmutableArray<string>.Empty;
    }

    private static bool Has(IReadOnlyList<Gs1Element> elements, string code) =>
        elements.Any(element => element.Code == code);
}
