using Scanio.Analysis.Gs1;

namespace Scanio.Analysis;

public static class BuiltInAnalyzers
{
    public static ScanAnalyzerPipeline CreatePipeline() =>
        new(new IScanAnalyzer[]
        {
            new HonestSignAnalyzer(),
            new Gs1Analyzer(),
            new EanUpcAnalyzer(),
            new IataBcbpAnalyzer(),
            new UrlAnalyzer(),
            new PlainTextAnalyzer()
        });
}
