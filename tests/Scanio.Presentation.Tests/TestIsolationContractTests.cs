namespace Scanio.Presentation.Tests;

[TestClass]
public sealed class TestIsolationContractTests
{
    [TestMethod]
    public void CultureAndStaticDisplaySettingsTests_DoNotRunInParallel()
    {
        foreach (var type in new[]
                 {
                     typeof(ConnectionViewModelTests),
                     typeof(LocalizationTests),
                     typeof(MonitorViewModelTests),
                     typeof(NotebookViewModelTests),
                     typeof(SettingsViewModelTests),
                     typeof(ShellViewModelTests)
                 })
        {
            Assert.IsTrue(
                type.IsDefined(typeof(DoNotParallelizeAttribute), inherit: false),
                $"{type.Name} mutates shared culture or display settings and must not run in parallel.");
        }
    }
}
