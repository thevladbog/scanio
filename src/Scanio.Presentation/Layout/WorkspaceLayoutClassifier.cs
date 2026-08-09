namespace Scanio.Presentation.Layout;

public static class WorkspaceLayoutClassifier
{
    public static WorkspaceLayoutMode Classify(double logicalWidth)
    {
        if (double.IsNaN(logicalWidth) || double.IsInfinity(logicalWidth) || logicalWidth < 1024d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(logicalWidth),
                logicalWidth,
                "Scanio requires a logical width of at least 1024 DIPs.");
        }

        if (logicalWidth < 1180d)
        {
            return WorkspaceLayoutMode.Compact;
        }

        return logicalWidth < 1320d ? WorkspaceLayoutMode.Medium : WorkspaceLayoutMode.Wide;
    }
}
