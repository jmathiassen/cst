using System.Collections.Generic;
using System.Threading;

namespace TgrDevelopments.Cst;

/// <summary>Language/format pack: parse entry point and identity.</summary>
public interface ILanguagePack
{
    /// <summary>Stable slug: <c>javascript</c>, <c>markdown</c>, <c>yaml</c>, …</summary>
    string LanguageId { get; }

    /// <summary>Human-readable display name.</summary>
    string DisplayName { get; }

    /// <summary>Extensions including dot; compared case-insensitively.</summary>
    IReadOnlyList<string> FileExtensions { get; }

    /// <summary>Optional filename matchers (e.g. <c>Dockerfile</c>, <c>*.csproj</c>).</summary>
    IReadOnlyList<string> FileNamePatterns { get; }

    /// <summary>Default quality advertised by this pack.</summary>
    SyntaxQuality DefaultQuality { get; }

    /// <summary>True when this pack should handle the path beyond extension matching.</summary>
    /// <param name="relativeOrFileName">Path or file name.</param>
    bool OwnsPath(string relativeOrFileName);

    /// <summary>Parse full text. Must not throw for ordinary invalid syntax.</summary>
    ConcreteSyntaxTree Parse(
        SourceText source,
        ParseOptions? options = null,
        CancellationToken cancellationToken = default);
}
