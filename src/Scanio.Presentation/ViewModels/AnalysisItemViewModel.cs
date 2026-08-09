using Scanio.Domain.Analysis;

namespace Scanio.Presentation.ViewModels;

public sealed class AnalysisItemViewModel
{
    public AnalysisItemViewModel(AnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        AnalyzerName = result.AnalyzerName;
        Format = result.Format;
        Confidence = result.Confidence switch
        {
            AnalysisConfidence.Exact => "Точный формат данных",
            AnalysisConfidence.Inferred => "Предположение по структуре",
            _ => "Не определено"
        };
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
