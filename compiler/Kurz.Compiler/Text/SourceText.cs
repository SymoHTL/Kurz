using System.Text;

namespace Kurz.Compiler.Text;

/// <summary>
/// One source file as the compiler reads it (L1): UTF-8, a byte-order mark at the start skipped, and
/// the two-byte line break of Windows editors read as one line break, in a `"""` block too (the
/// design record, section 8, the paragraph on source files). A file
/// that is not valid UTF-8 is the one compile error <c>invalid-source</c>, at the line of the first
/// bad byte, and the text is not read further.
/// </summary>
public sealed class SourceText
{
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly int[] lineStarts;

    private SourceText(string name, string text)
    {
        Name = name;
        Text = text;
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        lineStarts = starts.ToArray();
    }

    /// <summary>The name shown in front of a diagnostic: the file's name as given, never resolved to another path.</summary>
    public string Name { get; }

    /// <summary>The text, with each <c>\r\n</c> pair read as <c>\n</c>; a <c>\r</c> before such a pair stays, as any lone <c>\r</c> does.</summary>
    public string Text { get; }

    public int LineCount => lineStarts.Length;

    /// <summary>The source as a program text that was never a file, for tests and the interpolation holes.</summary>
    public static SourceText FromString(string name, string text) => new(name, text.Replace("\r\n", "\n", StringComparison.Ordinal));

    /// <summary>
    /// Decodes a file's bytes. Returns the text, or null with <paramref name="invalidSource"/> set to
    /// the <c>invalid-source</c> error at the line of the first byte that is not UTF-8.
    /// </summary>
    public static SourceText? FromBytes(string name, ReadOnlySpan<byte> bytes, out Diagnostic? invalidSource)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            bytes = bytes[3..];
        }

        try
        {
            invalidSource = null;
            return FromString(name, Strict.GetString(bytes));
        }
        catch (DecoderFallbackException)
        {
            invalidSource = Diagnostic.Error(ErrorIds.InvalidSource, LineOfFirstBadByte(bytes), "the file is not valid UTF-8");
            return null;
        }
    }

    /// <summary>The 1-based line that holds the character at <paramref name="offset"/>.</summary>
    public int LineOf(int offset)
    {
        var index = Array.BinarySearch(lineStarts, offset);
        return (index >= 0 ? index : ~index - 1) + 1;
    }

    /// <summary>The 1-based column of the character at <paramref name="offset"/>, counted in UTF-16 units.</summary>
    public int ColumnOf(int offset) => offset - lineStarts[LineOf(offset) - 1] + 1;

    private static int LineOfFirstBadByte(ReadOnlySpan<byte> bytes)
    {
        var line = 1;
        var i = 0;
        while (i < bytes.Length)
        {
            var status = Rune.DecodeFromUtf8(bytes[i..], out var rune, out var consumed);
            if (status != System.Buffers.OperationStatus.Done)
            {
                return line;
            }

            if (rune.Value == '\n')
            {
                line++;
            }

            i += consumed;
        }

        return line;
    }
}
