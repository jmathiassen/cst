using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;

namespace TgrDevelopments.Cst;

/// <summary>Scans container bodies for nested outline members (methods, etc.).</summary>
public static class MemberScanners
{
    /// <summary>
    /// Scans lines in <c>[startLineIdx, endLine1Based)</c> for members matching <paramref name="memberPattern"/>.
    /// Each match becomes an outline node at <paramref name="memberLevel"/> with brace-resolved end.
    /// </summary>
    public static List<OutlineNode> ScanBraceBodyMembers(
        string[] lines,
        LineMap map,
        int startLineIdx,
        int endLine1Based,
        Regex memberPattern,
        string memberKind,
        string nameGroup,
        int memberLevel,
        ref bool isPartial,
        CancellationToken cancellationToken = default,
        IReadOnlySet<string>? skipNames = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(memberPattern);
        ArgumentNullException.ThrowIfNull(memberKind);
        ArgumentNullException.ThrowIfNull(nameGroup);

        List<OutlineNode> members = [];
        int endIdx = Math.Min(endLine1Based, lines.Length);
        int j = Math.Max(0, startLineIdx);
        while (j < endIdx)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string raw = lines[j];
            string mt = raw.TrimStart();
            if (mt.Length == 0
                || mt.StartsWith("//", StringComparison.Ordinal)
                || mt.StartsWith("#", StringComparison.Ordinal)
                || mt.StartsWith("/*", StringComparison.Ordinal)
                || mt.StartsWith("*", StringComparison.Ordinal))
            {
                j++;
                continue;
            }

            Match mm = memberPattern.Match(mt);
            if (!mm.Success)
            {
                j++;
                continue;
            }

            string name = mm.Groups[nameGroup].Success
                ? mm.Groups[nameGroup].Value
                : (mm.Groups.Count > 1 ? mm.Groups[1].Value : "item");
            if (skipNames is not null && skipNames.Contains(name))
            {
                j++;
                continue;
            }

            int mend = BlockScanners.ResolveBraceEnd(lines, j, raw, mt, ref isPartial);
            if (mend > endLine1Based)
                mend = endLine1Based;
            if (mend < j + 1)
                mend = j + 1;
            members.Add(TextUtil.Outline(memberKind, name, map, j + 1, mend, memberLevel));
            j = mend;
        }

        return members;
    }

    /// <summary>
    /// Extends a declaration's end line through following indented continuation lines
    /// (F#/Python-style) until a line at indent ≤ <paramref name="baseIndent"/> that looks like a new top-level item.
    /// </summary>
    public static int ExtendIndentedBlock(
        string[] lines,
        int startLineIdx,
        int baseIndent,
        Func<string, bool>? isTopLevel)
    {
        ArgumentNullException.ThrowIfNull(lines);
        int endLn = startLineIdx + 1;
        for (int j = startLineIdx + 1; j < lines.Length; j++)
        {
            string raw = lines[j];
            if (raw.Trim().Length == 0)
            {
                endLn = j + 1;
                continue;
            }

            int indent = 0;
            while (indent < raw.Length && (raw[indent] == ' ' || raw[indent] == '\t'))
                indent++;

            string trimmed = raw.TrimStart();
            if (indent > baseIndent)
            {
                endLn = j + 1;
                continue;
            }

            // same or less indent
            if (isTopLevel is not null && isTopLevel(trimmed))
                break;
            if (indent <= baseIndent && trimmed.Length > 0)
                break;
        }

        return endLn;
    }

    /// <summary>
    /// Attaches orphan nodes from <paramref name="orphans"/> under parents in <paramref name="roots"/>
    /// when <paramref name="tryGetParentLabel"/> returns a parent label that matches.
    /// </summary>
    public static List<OutlineNode> AttachOrphansByParentLabel(
        IReadOnlyList<OutlineNode> roots,
        IReadOnlyList<OutlineNode> orphans,
        Func<OutlineNode, string?> tryGetParentLabel)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(orphans);
        ArgumentNullException.ThrowIfNull(tryGetParentLabel);

        if (orphans.Count == 0)
            return [.. roots];

        Dictionary<string, int> indexByLabel = new(StringComparer.Ordinal);
        List<OutlineNode> result = [.. roots];
        for (int i = 0; i < result.Count; i++)
        {
            if (!indexByLabel.ContainsKey(result[i].Label))
                indexByLabel[result[i].Label] = i;
        }

        List<OutlineNode> stillOrphan = [];
        foreach (OutlineNode orphan in orphans)
        {
            string? parentLabel = tryGetParentLabel(orphan);
            if (parentLabel is not null && indexByLabel.TryGetValue(parentLabel, out int idx))
            {
                result[idx] = OutlineNesting.WithChild(result[idx], orphan);
            }
            else
            {
                stillOrphan.Add(orphan);
            }
        }

        result.AddRange(stillOrphan);
        return result;
    }

    /// <summary>Common control-flow names that must never become method outlines.</summary>
    public static readonly HashSet<string> ControlFlowNames = new(StringComparer.Ordinal)
    {
        "if", "for", "while", "switch", "catch", "using", "return", "else", "foreach", "lock", "try",
    };
}
