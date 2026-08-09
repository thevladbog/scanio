using System.Text;

namespace Scanio.Application.Notebook;

public static class AtomicTextFileWriter
{
    public static void Write(string path, Action<TextWriter> writeContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(writeContent);

        var targetPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writeContent(writer);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }
}
