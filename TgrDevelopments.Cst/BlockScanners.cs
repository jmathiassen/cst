using System;

namespace TgrDevelopments.Cst;

/// <summary>Shared brace / end-keyword block scanners for line-oriented language packs.</summary>
public static class BlockScanners
{
    /// <summary>
    /// Finds the 1-based end line of a <c>{...}</c> block starting at <paramref name="startLineIdx"/> (0-based).
    /// Skips braces inside single- or double-quoted strings (simple escapes).
    /// </summary>
    /// <returns>1-based inclusive end line; sets <paramref name="isPartial"/> when the block never closes.</returns>
    public static int FindBraceBlockEnd(string[] lines, int startLineIdx, ref bool isPartial)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (startLineIdx < 0)
            startLineIdx = 0;

        int depth = 0;
        bool seen = false;
        for (int j = startLineIdx; j < lines.Length; j++)
        {
            string line = lines[j];
            bool inString = false;
            char quote = '\0';
            for (int k = 0; k < line.Length; k++)
            {
                char c = line[k];
                if (inString)
                {
                    if (c == '\\' && k + 1 < line.Length)
                    {
                        k++;
                        continue;
                    }

                    if (c == quote)
                        inString = false;
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    inString = true;
                    quote = c;
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                    seen = true;
                }
                else if (c == '}')
                {
                    depth--;
                    if (seen && depth <= 0)
                        return j + 1;
                }
            }
        }

        isPartial = true;
        return lines.Length == 0 ? 1 : lines.Length;
    }

    /// <summary>
    /// Finds the 1-based end line for keyword-terminated blocks (Ruby/VB/Lua-style <c>end</c>).
    /// Depth increases on known open keywords; decreases on <paramref name="endWords"/>.
    /// </summary>
    public static int FindEndKeyword(
        string[] lines,
        int startLineIdx,
        string[] endWords,
        string[] openPrefixes,
        ref bool isPartial)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(endWords);
        ArgumentNullException.ThrowIfNull(openPrefixes);

        int depth = 1;
        for (int j = startLineIdx + 1; j < lines.Length; j++)
        {
            string trimmed = lines[j].Trim();
            string low = trimmed.ToLowerInvariant();
            bool opens = false;
            foreach (string w in openPrefixes)
            {
                if (low.StartsWith(w, StringComparison.Ordinal) || low.Contains(" " + w.Trim(), StringComparison.Ordinal))
                {
                    opens = true;
                    break;
                }
            }

            if (opens && !low.StartsWith("end", StringComparison.Ordinal))
                depth++;

            foreach (string ew in endWords)
            {
                if (low == ew
                    || low.StartsWith(ew + " ", StringComparison.Ordinal)
                    || low.StartsWith(ew + "\t", StringComparison.Ordinal))
                {
                    depth--;
                    if (depth <= 0)
                        return j + 1;
                }
            }
        }

        isPartial = true;
        return lines.Length == 0 ? 1 : lines.Length;
    }

    /// <summary>Default open-keyword prefixes used by multi-language end scanners.</summary>
    public static readonly string[] DefaultOpenPrefixes =
    [
        "class ", "module ", "def ", "namespace ", "interface ", "structure ",
        "enum ", "sub ", "function ", "property ", "do ", "if ", "for ", "while ", "begin ",
    ];

    /// <summary>
    /// Resolves a declaration's end line: prefers brace block when <c>{</c> appears on the
    /// declaration line or within the next few lines; otherwise returns the start line.
    /// </summary>
    public static int ResolveBraceEnd(
        string[] lines,
        int lineIndex,
        string line,
        string trimmed,
        ref bool isPartial)
    {
        ArgumentNullException.ThrowIfNull(lines);
        int lineNumber = lineIndex + 1;
        bool hasBrace = line.Contains('{') || trimmed.Contains('{');
        if (!hasBrace)
        {
            int limit = Math.Min(lineIndex + 4, lines.Length);
            for (int a = lineIndex; a < limit; a++)
            {
                if (lines[a].Contains('{'))
                {
                    hasBrace = true;
                    return FindBraceBlockEnd(lines, a, ref isPartial);
                }
            }
        }
        else
        {
            return FindBraceBlockEnd(lines, lineIndex, ref isPartial);
        }

        return lineNumber;
    }
}
