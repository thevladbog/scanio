using Scanio.Domain.Analysis;
using Scanio.Presentation.Localization;

namespace Scanio.Presentation.ViewModels;

public sealed class AnalysisItemViewModel
{
    public AnalysisItemViewModel(AnalysisResult result, IUiLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(localizer);

        AnalyzerName = result.AnalyzerName;
        Format = result.Format;
        Confidence = localizer[result.Confidence switch
        {
            AnalysisConfidence.Exact => UiTextKeys.ConfidenceExact,
            AnalysisConfidence.Inferred => UiTextKeys.ConfidenceInferred,
            _ => UiTextKeys.ConfidenceUnknown
        }];
        Evidence = result.Evidence;
        Summary = result.Summary;
        Fields = result.Fields
            .Select(field => new AnalysisFieldItemViewModel(field.Code, field.Name, field.Value))
            .ToArray();
        Errors = result.ValidationErrors.ToArray();
        Warnings = result.ValidationWarnings.ToArray();
    }

    public string AnalyzerName { get; }
    public string Format { get; }
    public string Confidence { get; }
    public string Evidence { get; }
    public string Summary { get; }
    public IReadOnlyList<AnalysisFieldItemViewModel> Fields { get; }
    public IReadOnlyList<string> Errors { get; }
    public IReadOnlyList<string> Warnings { get; }
}

public sealed record AnalysisFieldItemViewModel(string Code, string Name, string Value);
