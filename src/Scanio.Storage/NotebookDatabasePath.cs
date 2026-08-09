namespace Scanio.Storage;

public static class NotebookDatabasePath
{
    public static string Resolve(bool portable, string applicationBaseDirectory, string localApplicationDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationDataDirectory);

        return portable
            ? Path.Combine(applicationBaseDirectory, "Data", "scanio.db")
            : Path.Combine(localApplicationDataDirectory, "Scanio", "scanio.db");
    }
}
