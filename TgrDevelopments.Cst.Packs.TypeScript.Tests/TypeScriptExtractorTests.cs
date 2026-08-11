using System.Collections.Generic;
using System.Linq;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.TypeScript;

namespace TgrDevelopments.Cst.Packs.TypeScript.Tests;

public class TypeScriptExtractorTests
{
    private readonly TypeScriptLanguagePack _pack = new();
    private readonly TypeScriptStructuralExtractor _extractor = new();

    [Fact]
    public void Extract_Empty_EmptyLists()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(""));
        StructuralExtract extract = _extractor.Extract(tree);
        Assert.Empty(extract.Declarations);
        Assert.Empty(extract.Calls);
        Assert.Equal(SyntaxQuality.Structural, extract.Quality);
    }

    [Fact]
    public void Extract_Happy_InterfaceTypeAndFunction()
    {
        string text = """
            interface IEngine { x: number }
            type Id = string;
            enum Kind { A, B }
            class Engine {
              run(): void { helper(); }
            }
            function helper(): void {}
            """;
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(text));
        StructuralExtract extract = _extractor.Extract(tree);

        Assert.Contains(extract.Declarations, d => d.Name == "IEngine" && d.Kind == "interface");
        Assert.Contains(extract.Declarations, d => d.Name == "Id" && d.Kind == "type");
        Assert.Contains(extract.Declarations, d => d.Name == "Kind" && d.Kind == "enum");
        Assert.Contains(extract.Declarations, d => d.Name == "Engine" && d.Kind == "class");
        Assert.Contains(extract.Declarations, d => d.Name == "helper" && d.Kind == "function");
        Assert.Contains(extract.Calls, c => c.CalleeName == "helper");
    }

    [Fact]
    public void Extract_Honesty_StringCommentNotCalls()
    {
        string text = """
            function real(): void {}
            const s = "foo(";
            // baz(
            real();
            """;
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(text));
        StructuralExtract extract = _extractor.Extract(tree);

        Assert.Contains(extract.Calls, c => c.CalleeName == "real");
        Assert.DoesNotContain(extract.Calls, c => c.CalleeName is "foo" or "baz");
    }

    [Fact]
    public void Extract_TypePosition_NotCall()
    {
        string text = """
            function Foo(): void {}
            function bar(x: Foo): void {
              const y = x as Foo;
            }
            """;
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(text));
        StructuralExtract extract = _extractor.Extract(tree);

        // Foo in type position should not be a call; no Foo() call expression
        Assert.DoesNotContain(extract.Calls, c => c.CalleeName == "Foo");
    }

    [Fact]
    public void Extract_Broken_NoThrow()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From("function f( {\n"));
        StructuralExtract extract = _extractor.Extract(tree);
        Assert.Equal(SyntaxConfidence.Syntax, extract.Confidence);
    }
}
