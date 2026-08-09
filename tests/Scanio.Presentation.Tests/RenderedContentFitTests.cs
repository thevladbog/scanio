using Scanio.Presentation.Windows.Tests;

namespace Scanio.Presentation.Tests;

[TestClass]
public sealed class RenderedContentFitTests
{
    [TestMethod]
    public void Fits_AcceptsContentWithSpareSpace()
    {
        Assert.IsTrue(RenderedContentFit.Fits(
            availableWidth: 220,
            availableHeight: 52,
            requiredWidth: 180,
            requiredHeight: 40));
    }

    [TestMethod]
    public void Fits_RejectsContentThatNeedsMoreWrappedHeight()
    {
        Assert.IsFalse(RenderedContentFit.Fits(
            availableWidth: 180,
            availableHeight: 31,
            requiredWidth: 180,
            requiredHeight: 42));
    }

    [TestMethod]
    public void Fits_RejectsContentThatNeedsMoreWidth()
    {
        Assert.IsFalse(RenderedContentFit.Fits(
            availableWidth: 160,
            availableHeight: 40,
            requiredWidth: 180,
            requiredHeight: 40));
    }
}
