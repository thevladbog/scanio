using System.Text;
using Scanio.Domain.Analysis;

namespace Scanio.Analysis;

public static class TextDecoder
{
    static TextDecoder()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static DecodedPayload Decode(ReadOnlySpan<byte> bytes, PayloadTextEncoding encoding)
    {
        var strictEncoding = CreateEncoding(encoding, throwOnInvalidBytes: true);
        string text;
        string? warning = null;

        try
        {
            text = strictEncoding.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            text = CreateEncoding(encoding, throwOnInvalidBytes: false).GetString(bytes);
            warning = $"Some bytes are invalid for {GetEncodingLabel(encoding)} and are displayed as replacement characters.";
        }

        return DecodedPayload.Create(bytes, encoding, text, CreateEscapedDisplay(text), warning);
    }

    private static Encoding CreateEncoding(PayloadTextEncoding encoding, bool throwOnInvalidBytes) =>
        encoding switch
        {
            PayloadTextEncoding.Utf8 => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes),
            PayloadTextEncoding.Ascii => Encoding.GetEncoding(
                Encoding.ASCII.CodePage,
                new EncoderExceptionFallback(),
                throwOnInvalidBytes ? new DecoderExceptionFallback() : new DecoderReplacementFallback("\uFFFD")),
            PayloadTextEncoding.Windows1251 => Encoding.GetEncoding(
                1251,
                new EncoderExceptionFallback(),
                throwOnInvalidBytes ? new DecoderExceptionFallback() : new DecoderReplacementFallback("\uFFFD")),
            PayloadTextEncoding.Latin1 => Encoding.GetEncoding(
                "iso-8859-1",
                new EncoderExceptionFallback(),
                throwOnInvalidBytes ? new DecoderExceptionFallback() : new DecoderReplacementFallback("\uFFFD")),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unsupported text encoding.")
        };

    private static string GetEncodingLabel(PayloadTextEncoding encoding) =>
        encoding switch
        {
            PayloadTextEncoding.Utf8 => "UTF-8",
            PayloadTextEncoding.Ascii => "ASCII",
            PayloadTextEncoding.Windows1251 => "Windows-1251",
            PayloadTextEncoding.Latin1 => "Latin-1",
            _ => encoding.ToString()
        };

    private static string CreateEscapedDisplay(string text)
    {
        var display = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            display.Append(character switch
            {
                '\r' => "<CR>",
                '\n' => "<LF>",
                '\u001D' => "<GS>",
                '\u001E' => "<RS>",
                '\u0004' => "<EOT>",
                '\u001B' => "<ESC>",
                _ when char.IsControl(character) => $"<U+{(int)character:X4}>",
                _ => character.ToString()
            });
        }

        return display.ToString();
    }
}
