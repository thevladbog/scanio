using Scanio.Domain.Analysis;
using Scanio.Presentation.Localization;
using Scanio.Presentation.Settings;

namespace Scanio.Presentation.ViewModels;

public sealed class AnalysisItemViewModel
{
    public AnalysisItemViewModel(AnalysisResult result, IUiLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(localizer);

        AnalyzerName = result.AnalyzerName;
        Format = TranslateFormat(result, localizer);
        Confidence = localizer[result.Confidence switch
        {
            AnalysisConfidence.Exact => UiTextKeys.ConfidenceExact,
            AnalysisConfidence.Inferred => UiTextKeys.ConfidenceInferred,
            _ => UiTextKeys.ConfidenceUnknown
        }];
        Evidence = TranslateNarrative(result, localizer, "Evidence");
        Summary = TranslateNarrative(result, localizer, "Summary");
        Fields = result.Fields
            .Select(field => new AnalysisFieldItemViewModel(
                field.Code,
                TranslateFieldName(field.Code, field.Name, localizer),
                TranslateFieldValue(field.Code, field.Value, localizer)))
            .ToArray();
        Errors = TranslateMessages(result.ValidationErrors, "Analysis.ValidationError", localizer);
        Warnings = TranslateMessages(result.ValidationWarnings, "Analysis.ValidationWarning", localizer);
    }

    public string AnalyzerName { get; }
    public string Format { get; }
    public string Confidence { get; }
    public string Evidence { get; }
    public string Summary { get; }
    public IReadOnlyList<AnalysisFieldItemViewModel> Fields { get; }
    public IReadOnlyList<string> Errors { get; }
    public IReadOnlyList<string> Warnings { get; }

    private static string TranslateFormat(AnalysisResult result, IUiLocalizer localizer)
    {
        if (localizer.Language == UiLanguage.English)
        {
            return result.Format;
        }

        return result.AnalyzerName switch
        {
            "GS1" => localizer["Analysis.Format.GS1"],
            "PlainText" => localizer["Analysis.Format.PlainText"],
            "HonestSign" => localizer["Analysis.Format.HonestSign"],
            _ => result.Format
        };
    }

    private static string TranslateNarrative(AnalysisResult result, IUiLocalizer localizer, string part)
    {
        if (localizer.Language == UiLanguage.English)
        {
            return part == "Evidence" ? result.Evidence : result.Summary;
        }

        var key = $"Analysis.{result.AnalyzerName}.{part}";
        var translated = localizer[key];
        return translated == key
            ? localizer[$"Analysis.Generic.{part}"]
            : translated;
    }

    private static string TranslateFieldName(string code, string name, IUiLocalizer localizer)
    {
        if (localizer.Language == UiLanguage.English)
        {
            return name;
        }

        var normalized = NormalizeFieldCode(code);
        var key = $"Analysis.Field.{normalized}";
        var translated = localizer[key];
        return translated == key ? name : translated;
    }

    private static string NormalizeFieldCode(string code)
    {
        string[] knownSuffixes =
        [
            "check-in-sequence",
            "passenger-status",
            "conditional-data",
            "flight-number",
            "flight-date",
            "electronic-ticket",
            "passenger-name",
            "product-group-candidate",
            "product-group",
            "destination",
            "compartment",
            "trailing-data",
            "carrier",
            "origin",
            "seat",
            "pnr"
        ];

        var suffix = knownSuffixes.FirstOrDefault(candidate =>
            code.Equals(candidate, StringComparison.Ordinal) ||
            code.EndsWith($"-{candidate}", StringComparison.Ordinal));
        if (suffix is not null)
        {
            return suffix;
        }

        return code.StartsWith("392", StringComparison.Ordinal) ||
               code.StartsWith("393", StringComparison.Ordinal)
            ? "price"
            : code;
    }

    private static string TranslateFieldValue(string code, string value, IUiLocalizer localizer)
    {
        if (localizer.Language == UiLanguage.English ||
            code is not ("product-group" or "product-group-candidate"))
        {
            return value;
        }

        var key = $"Analysis.ProductGroup.{value.Replace(" ", "_", StringComparison.Ordinal)}";
        var translated = localizer[key];
        return translated == key ? value : translated;
    }

    private static IReadOnlyList<string> TranslateMessages(
        IEnumerable<string> source,
        string key,
        IUiLocalizer localizer)
    {
        var messages = source.ToArray();
        if (localizer.Language == UiLanguage.English)
        {
            return messages;
        }

        return messages.Select((_, index) => $"{localizer[key]} {index + 1}").ToArray();
    }
}

public sealed record AnalysisFieldItemViewModel(string Code, string Name, string Value);
