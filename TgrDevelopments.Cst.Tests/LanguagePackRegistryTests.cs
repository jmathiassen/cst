using System.Collections.Generic;
using System.Threading;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Csv;
using TgrDevelopments.Cst.Packs.Json;
using TgrDevelopments.Cst.Packs.Markdown;

namespace TgrDevelopments.Cst.Tests;

public class LanguagePackRegistryTests
{
    [Fact]
    public void ResolvePack_ByExtension_ReturnsMarkdown()
    {
        // Arrange
        LanguagePackRegistry registry = new();
        MarkdownRegistration.Register(registry);

        // Act
        ILanguagePack? pack = registry.ResolvePack("readme.MD");

        // Assert
        Assert.NotNull(pack);
        Assert.Equal("markdown", pack!.LanguageId);
    }

    [Fact]
    public void ResolvePack_Unknown_ReturnsNull()
    {
        // Arrange
        LanguagePackRegistry registry = new();
        MarkdownRegistration.Register(registry);

        // Act
        ILanguagePack? pack = registry.ResolvePack("file.unknown");

        // Assert
        Assert.Null(pack);
    }

    [Fact]
    public void CstService_TryParse_NoPack_ReturnsNull()
    {
        // Arrange
        LanguagePackRegistry registry = new();
        CstService service = new(registry);
        SourceText source = SourceText.From("x");

        // Act
        ConcreteSyntaxTree? tree = service.TryParse("x.bin", source);

        // Assert
        Assert.Null(tree);
    }

    [Fact]
    public void CstService_TryParse_AndOutline_Markdown()
    {
        // Arrange
        LanguagePackRegistry registry = new();
        MarkdownRegistration.Register(registry);
        CsvRegistration.Register(registry);
        JsonRegistration.Register(registry);
        CstService service = new(registry);
        SourceText source = SourceText.From("# Title\n\n## Section\n");

        // Act
        ConcreteSyntaxTree? tree = service.TryParse("doc.md", source);
        IReadOnlyList<OutlineNode> outline = service.GetOutline(tree!);

        // Assert
        Assert.NotNull(tree);
        Assert.Equal("markdown", tree!.LanguageId);
        Assert.NotEmpty(outline);
        Assert.Equal("Title", outline[0].Label);
    }

    [Fact]
    public void ResolvePack_CsvAndJson_ByExtension()
    {
        // Arrange
        LanguagePackRegistry registry = new();
        CsvRegistration.Register(registry);
        JsonRegistration.Register(registry);

        // Act / Assert
        Assert.Equal("csv", registry.ResolvePack("a.csv")!.LanguageId);
        Assert.Equal("csv", registry.ResolvePack("a.tsv")!.LanguageId);
        Assert.Equal("json", registry.ResolvePack("a.json")!.LanguageId);
        Assert.Equal("json", registry.ResolvePack("a.jsonc")!.LanguageId);
    }

    [Fact]
    public void Parse_AttachesCachedOutline_GetOutlineReuses()
    {
        LanguagePackRegistry registry = new();
        MarkdownRegistration.Register(registry);
        CstService service = new(registry);
        ConcreteSyntaxTree? tree = service.TryParse("doc.md", SourceText.From("# A\n\n## B\n"));
        Assert.NotNull(tree);
        Assert.NotNull(tree!.CachedOutline);
        IReadOnlyList<OutlineNode> o1 = service.GetOutline(tree);
        IReadOnlyList<OutlineNode> o2 = service.GetOutline(tree);
        Assert.Same(tree.CachedOutline, o1);
        Assert.Same(o1, o2);
        Assert.Equal("A", o1[0].Label);
    }
}

