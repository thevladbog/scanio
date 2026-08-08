using Scanio.Analysis;
using Scanio.Domain.Analysis;

namespace Scanio.Analysis.Tests;

[TestClass]
public sealed class TextDecoderTests
{
    [TestMethod]
    public void Decode_DecodesUtf8AndPreservesSourceBytes()
    {
        var source = new byte[] { 0xD0, 0xA1, 0xD0, 0xBA, 0xD0, 0xB0, 0xD0, 0xBD };

        var decoded = TextDecoder.Decode(source, PayloadTextEncoding.Utf8);
        source[0] = 0x00;

        Assert.AreEqual("Скан", decoded.Text);
        Assert.IsFalse(decoded.HasDecodingWarning);
        CollectionAssert.AreEqual(new byte[] { 0xD0, 0xA1, 0xD0, 0xBA, 0xD0, 0xB0, 0xD0, 0xBD }, decoded.Bytes.ToArray());
    }

    [TestMethod]
    public void Decode_DecodesAsciiWindows1251AndLatin1()
    {
        Assert.AreEqual("SCAN-01", TextDecoder.Decode("SCAN-01"u8, PayloadTextEncoding.Ascii).Text);
        Assert.AreEqual("Привет", TextDecoder.Decode(new byte[] { 0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2 }, PayloadTextEncoding.Windows1251).Text);
        Assert.AreEqual("Åä", TextDecoder.Decode(new byte[] { 0xC5, 0xE4 }, PayloadTextEncoding.Latin1).Text);
    }

    [TestMethod]
    public void Decode_InvalidUtf8KeepsBytesAndReturnsAReplacementCharacterWarning()
    {
        var source = new byte[] { 0xC3, 0x28 };

        var decoded = TextDecoder.Decode(source, PayloadTextEncoding.Utf8);
        source[0] = 0x00;

        Assert.AreEqual("\uFFFD(", decoded.Text);
        Assert.IsTrue(decoded.HasDecodingWarning);
        Assert.IsFalse(string.IsNullOrWhiteSpace(decoded.DecodingWarning));
        CollectionAssert.AreEqual(new byte[] { 0xC3, 0x28 }, decoded.Bytes.ToArray());
    }

    [TestMethod]
    public void Decode_InvalidAsciiReturnsAReplacementCharacterWarning()
    {
        var decoded = TextDecoder.Decode(new byte[] { 0x53, 0xFF }, PayloadTextEncoding.Ascii);

        Assert.AreEqual("S\uFFFD", decoded.Text);
        Assert.IsTrue(decoded.HasDecodingWarning);
    }

    [TestMethod]
    public void Decode_EscapedDisplayLabelsScannerControlCharacters()
    {
        var decoded = TextDecoder.Decode(new byte[] { 0x0D, 0x0A, 0x1D, 0x1E, 0x04, 0x1B }, PayloadTextEncoding.Utf8);

        Assert.AreEqual("<CR><LF><GS><RS><EOT><ESC>", decoded.EscapedDisplay);
    }
}
