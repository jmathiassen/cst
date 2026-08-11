using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Sln;

/// <summary>Optional structural extractor for solution files.</summary>
public sealed class SlnStructuralExtractor : IStructuralExtractor
{
    private static readonly Regex ProjectLine = new(
        @"^Project\(""\{[^}]+\}""\)\s*=\s*""([^""]+)""\s*,\s*""([^""]+)""\s*,\s*""\{([^}]+)\}""",
        RegexOptions.Compiled);

    private static readonly Regex NestedLine = new(
        @"^\s*\{([^}]+)\}\s*=\s*\{([^}]+)\}",
        RegexOptions.Compiled);

    /// <inheritdoc />
    public string LanguageId => "sln";

    /// <inheritdoc />
    public StructuralExtract Extract(
        ConcreteSyntaxTree tree,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        cancellationToken.ThrowIfCancellationRequested();

        string text = tree.Source.Text;
        string from = tree.Source.FilePath ?? ".";
        if (ParseHelpers.IsNullOrWhiteSpace(text))
        {
            return new StructuralExtract(LanguageId, quality: SyntaxQuality.Structural);
        }

        LineMap map = new(text);
        string[] lines = TextUtil.SplitLines(text);
        List<DeclSymbol> decls = [];
        List<ExtractEdge> edges = [];
        bool inNested = false;

        for (int i = 0; i < lines.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string t = lines[i].Trim();
            int ln = i + 1;

            if (t.StartsWith("GlobalSection(NestedProjects)", StringComparison.OrdinalIgnoreCase))
            {
                inNested = true;
                continue;
            }
            if (t.StartsWith("EndGlobalSection", StringComparison.OrdinalIgnoreCase))
            {
                inNested = false;
                continue;
            }

            Match m = ProjectLine.Match(lines[i].TrimEnd());
            if (!m.Success)
                m = ProjectLine.Match(t);
            if (m.Success)
            {
                string name = m.Groups[1].Value;
                string path = m.Groups[2].Value;
                string guid = m.Groups[3].Value;
                TextSpan span = map.GetSpanForLines(ln, ln);
                decls.Add(ExtractHelpers.Decl(name, "project", span, map));
                Dictionary<string, string> props = new()
                {
                    ["guid"] = guid,
                    ["name"] = name
                };
                edges.Add(new ExtractEdge("solution_project", from, path, span, props));
                continue;
            }

            // fallback Project line without full GUID pattern
            if (t.StartsWith("Project(", StringComparison.Ordinal))
            {
                Match m2 = Regex.Match(t, @"""([^""]+)""\s*,\s*""([^""]+)""\s*,\s*""\{?([^""}]+)");
                if (m2.Success)
                {
                    string name = m2.Groups[1].Value;
                    string path = m2.Groups[2].Value;
                    string guid = m2.Groups[3].Value.Trim('{', '}');
                    TextSpan span = map.GetSpanForLines(ln, ln);
                    decls.Add(ExtractHelpers.Decl(name, "project", span, map));
                    Dictionary<string, string> props = new()
                    {
                        ["guid"] = guid,
                        ["name"] = name
                    };
                    edges.Add(new ExtractEdge("solution_project", from, path, span, props));
                }
                continue;
            }

            if (inNested)
            {
                Match n = NestedLine.Match(t);
                if (n.Success)
                {
                    string child = n.Groups[1].Value;
                    string parent = n.Groups[2].Value;
                    TextSpan span = map.GetSpanForLines(ln, ln);
                    edges.Add(new ExtractEdge("nested_project", child, parent, span));
                }
            }
        }

        return new StructuralExtract(
            LanguageId,
            decls,
            edges: edges,
            quality: SyntaxQuality.Structural);
    }
}
