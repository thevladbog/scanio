namespace Scanio.Presentation.Tests;

[TestClass]
public sealed class InstallerDefinitionContractTests
{
    [TestMethod]
    public void Installer_IsPerUser_AndDoesNotDeleteUserData()
    {
        var scriptPath = Path.Combine(RepositoryRoot(), "installer", "Scanio.iss");

        Assert.IsTrue(File.Exists(scriptPath), $"Missing installer definition: {scriptPath}");

        var script = File.ReadAllText(scriptPath);

        StringAssert.Contains(script, "AppId={{B786AC90-6A74-4E80-AE30-8D3C15A8C9C2}");
        StringAssert.Contains(script, "PrivilegesRequired=lowest");
        StringAssert.Contains(script, @"DefaultDirName={localappdata}\Programs\Scanio");
        StringAssert.Contains(script, "CloseApplications=yes");
        StringAssert.Contains(script, "RestartApplications=no");
        Assert.IsFalse(script.Contains("[UninstallDelete]", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains(@"{localappdata}\Scanio", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Installer_ProvidesLocalizedShortcuts_AndPreservesPortableBehavior()
    {
        var scriptPath = Path.Combine(RepositoryRoot(), "installer", "Scanio.iss");

        Assert.IsTrue(File.Exists(scriptPath), $"Missing installer definition: {scriptPath}");

        var script = File.ReadAllText(scriptPath);

        StringAssert.Contains(script, "Name: \"english\"; MessagesFile: \"compiler:Default.isl\"");
        StringAssert.Contains(script, "Name: \"russian\"; MessagesFile: \"compiler:Languages\\Russian.isl\"");
        StringAssert.Contains(script, "Name: \"desktopicon\"; Description: \"{cm:CreateDesktopIcon}\"; GroupDescription: \"{cm:AdditionalIcons}\"; Flags: unchecked");
        StringAssert.Contains(script, "SetupIconFile=..\\src\\Scanio.Presentation\\Assets\\scanio.ico");
        StringAssert.Contains(script, "Name: \"{userprograms}\\Scanio\"; Filename: \"{app}\\Scanio.exe\"; WorkingDir: \"{app}\"");
        StringAssert.Contains(script, "Name: \"{userdesktop}\\Scanio\"; Filename: \"{app}\\Scanio.exe\"; WorkingDir: \"{app}\"; Tasks: desktopicon");
        StringAssert.Contains(script, "Excludes: \"portable.flag,Data\\*\"");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (true)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                return directory.FullName;
            }

            if (directory.Parent is null)
            {
                throw new DirectoryNotFoundException("Could not locate the repository root.");
            }

            directory = directory.Parent;
        }
    }
}
