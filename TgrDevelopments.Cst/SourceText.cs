using System;
using System.Text;

namespace TgrDevelopments.Cst;

/// <summary>Immutable snapshot of input text.</summary>
public sealed class SourceText
{
    /// <summary>Gets the full text content.</summary>
    public string Text { get; }

    /// <summary>Gets the optional file path used for diagnostics only.</summary>
    public string? FilePath { get; }

    /// <summary>Gets the encoding associated with the source (default UTF-8).</summary>
    public Encoding Encoding { get; }

    private SourceText(string text, string? filePath, Encoding encoding)
    {
        Text = text;
        FilePath = filePath;
        Encoding = encoding;
    }

    /// <summary>Creates a <see cref="SourceText"/> from a string.</summary>
    /// <param name="text">Source content.</param>
    /// <param name="filePath">Optional path for diagnostics.</param>
    /// <returns>A new source snapshot.</returns>
    public static SourceText From(string text, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new SourceText(text, filePath, Encoding.UTF8);
    }

    /// <summary>Creates a <see cref="SourceText"/> from UTF-8 bytes.</summary>
    /// <param name="utf8">UTF-8 encoded content.</param>
    /// <param name="filePath">Optional path for diagnostics.</param>
    /// <returns>A new source snapshot.</returns>
    public static SourceText From(ReadOnlyMemory<byte> utf8, string? filePath = null)
    {
        string text = Encoding.UTF8.GetString(utf8.Span);
        return new SourceText(text, filePath, Encoding.UTF8);
    }
}
