using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Xml;

/// <summary>Optional structural extractor for XML/csproj — package/project reference edges.</summary>
public sealed class XmlStructuralExtractor : IStructuralExtractor
{
    private static readonly Regex ProjectRef = new(
        @"<ProjectReference\b[^>]*\bInclude\s*=\s*""([^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PackageRefSplitVersion = new(
        @"<PackageReference\b[^>]*\bInclude\s*=\s*""([^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VersionAttr = new(
        @"Version\s*=\s*""([^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Tfm = new(
        @"<TargetFramework(?:s)?>\s*([^<]+)\s*</TargetFramework(?:s)?>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <inheritdoc />
    public string LanguageId => "xml";

    /// <inheritdoc />
    public StructuralExtract Extract(
        ConcreteSyntaxTree tree,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        cancellationToken.ThrowIfCancellationRequested();

        string text = tree.Source.Text;
        string from = tree.Source.FilePath ?? ".";
        LineMap map = new(text);
        List<ExtractEdge> edges = [];
        List<DeclSymbol> decls = [];

        if (ParseHelpers.IsNullOrWhiteSpace(text))
        {
            return new StructuralExtract(
                LanguageId,
                quality: SyntaxQuality.Structural);
        }

        foreach (Match m in ProjectRef.Matches(text))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string include = m.Groups[1].Value;
            TextSpan span = new(m.Index, m.Length);
            Dictionary<string, string> props = new() { ["include"] = include };
            edges.Add(new ExtractEdge("project_reference", from, include, span, props));
        }

        foreach (Match m in PackageRefSplitVersion.Matches(text))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string packageId = m.Groups[1].Value;
            TextSpan span = new(m.Index, m.Length);
            string? version = null;
            Match vm = VersionAttr.Match(m.Value);
            if (vm.Success)
                version = vm.Groups[1].Value;
            Dictionary<string, string> props = new() { ["include"] = packageId };
            if (version is not null)
                props["version"] = version;
            edges.Add(new ExtractEdge("package_reference", from, packageId, span, props));
        }

        foreach (Match m in Tfm.Matches(text))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string tfm = m.Groups[1].Value.Trim();
            TextSpan span = new(m.Index, m.Length);
            decls.Add(ExtractHelpers.Decl(tfm, "target_framework", span, map));
        }

        return new StructuralExtract(
            LanguageId,
            decls,
            edges: edges,
            quality: SyntaxQuality.Structural);
    }
}
