using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;

namespace Scanio.Presentation.Windows.Tests;

[TestClass]
public sealed class ExecutableIconTests
{
    [TestMethod]
    public void PublishedExecutableContainsTheApprovedScanioIcon()
    {
        var repository = FindRepositoryRoot();
        var publish = Path.Combine(AppContext.BaseDirectory, "TestResults", "icon-publish");
        if (Directory.Exists(publish))
        {
            Directory.Delete(publish, recursive: true);
        }

        Directory.CreateDirectory(publish);
        RunDotnet(
            repository,
            "publish",
            Path.Combine(repository, "src", "Scanio.Presentation", "Scanio.Presentation.csproj"),
            "-c", "Release",
            "-f", "net10.0-windows10.0.19041.0",
            "-r", "win-x64",
            "--self-contained", "true",
            "--no-restore",
            "-p:PublishSingleFile=false",
            "-o", publish);

        var executable = Path.Combine(publish, "Scanio.exe");
        Assert.IsTrue(File.Exists(executable), $"Published executable not found: {executable}");
        using var extracted = Icon.ExtractAssociatedIcon(executable);
        Assert.IsNotNull(extracted, "Scanio.exe has no extractable PE icon.");
        using var approved = new Icon(
            Path.Combine(repository, "src", "Scanio.Presentation", "Assets", "scanio.ico"),
            32,
            32);

        Assert.AreEqual(32, extracted.Width, "The executable did not expose a 32 px application icon frame.");
        Assert.AreEqual(32, extracted.Height, "The executable did not expose a 32 px application icon frame.");
        Assert.AreEqual(BitmapDigest(approved), BitmapDigest(extracted),
            "The icon extracted from Scanio.exe differs from the approved Scanio application icon.");
    }

    private static string BitmapDigest(Icon icon)
    {
        using var bitmap = icon.ToBitmap();
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void RunDotnet(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet publish.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(TimeSpan.FromMinutes(3));
        Assert.AreEqual(0, process.ExitCode, $"dotnet publish failed.\n{output}\n{error}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Scanio.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the Scanio repository root.");
    }
}
