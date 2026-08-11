using System;
using System.Collections.Generic;

namespace TgrDevelopments.Cst;

/// <summary>Shared helpers for packs implementing Core parse policy.</summary>
public static class ParseHelpers
{
    /// <summary>Diagnostic code for oversize input.</summary>
    public const string LimitCode = "CST_LIMIT";

    /// <summary>
    /// Applies default MaxChars policy: prefix truncation + partial + <c>CST_LIMIT</c> diagnostic.
    /// </summary>
    /// <param name="source">Original source.</param>
    /// <param name="options">Parse options (null uses defaults).</param>
    /// <param name="effectiveText">Text to parse (possibly truncated).</param>
    /// <param name="diagnostics">Mutable diagnostic list to append to.</param>
    /// <param name="isPartial">Set true when truncated.</param>
    /// <returns>Effective source (same instance when not truncated; new when truncated).</returns>
    public static SourceText ApplyMaxChars(
        SourceText source,
        ParseOptions? options,
        out string effectiveText,
        List<ParseDiagnostic> diagnostics,
        out bool isPartial)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(diagnostics);

        ParseOptions opts = options ?? new ParseOptions();
        string text = source.Text;
        if (text.Length <= opts.MaxChars)
        {
            effectiveText = text;
            isPartial = false;
            return source;
        }

        effectiveText = text.Substring(0, opts.MaxChars);
        isPartial = true;
        diagnostics.Add(new ParseDiagnostic(
            LimitCode,
            $"Input exceeds MaxChars ({opts.MaxChars}); parsing prefix only.",
            new TextSpan(opts.MaxChars, 0),
            DiagnosticSeverity.Warning));

        return SourceText.From(effectiveText, source.FilePath);
    }

    /// <summary>Returns true when text is null/empty or whitespace only.</summary>
    public static bool IsNullOrWhiteSpace(string text)
    {
        return string.IsNullOrWhiteSpace(text);
    }
}
