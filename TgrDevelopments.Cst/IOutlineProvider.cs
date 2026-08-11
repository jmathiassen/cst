using System.Collections.Generic;
using System.Threading;

namespace TgrDevelopments.Cst;

/// <summary>Produces a navigable outline from a concrete syntax tree.</summary>
public interface IOutlineProvider
{
    /// <summary>Language id this provider serves.</summary>
    string LanguageId { get; }

    /// <summary>Builds outline nodes with 1-based line anchors.</summary>
    IReadOnlyList<OutlineNode> GetOutline(
        ConcreteSyntaxTree tree,
        CancellationToken cancellationToken = default);
}
