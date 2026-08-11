using System.Collections.Generic;
using System.Linq;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.JavaScript;

namespace TgrDevelopments.Cst.Packs.JavaScript.Tests;

public class JavaScriptExtractorTests
{
    private readonly JavaScriptLanguagePack _pack = new();
    private readonly JavaScriptStructuralExtractor _extractor = new();

    [Fact]
    public void Extract_Empty_EmptyLists()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(""));
        StructuralExtract extract = _extractor.Extract(tree);

        Assert.Empty(extract.Declarations);
        Assert.Empty(extract.Calls);
        Assert.Equal(SyntaxConfidence.Syntax, extract.Confidence);
        Assert.Equal(SyntaxQuality.Structural, extract.Quality);
    }

    [Fact]
    public void Extract_Happy_DeclsAndCalls()
    {
        string text = """
            class Engine {
              run() {
                helper();
              }
            }
            function helper() {}
            function main() {
              helper();
            }
            """;
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(text));
        StructuralExtract extract = _extractor.Extract(tree);

        Assert.Contains(extract.Declarations, d => d.Name == "Engine" && d.Kind == "class");
        Assert.Contains(extract.Declarations, d => d.Name == "helper" && d.Kind == "function");
        Assert.Contains(extract.Declarations, d => d.Name == "main" && d.Kind == "function");
        Assert.Contains(extract.Calls, c => c.CalleeName == "helper");
        Assert.All(extract.Declarations, d => Assert.True(d.Span.Length >= 0));
    }

    [Fact]
    public void Extract_Honesty_StringAndCommentNotCalls()
    {
        string text = """
            function real() {}
            const s = "foo(";
            const t = 'bar(';
            // baz(
            real();
            """;
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(text));
        StructuralExtract extract = _extractor.Extract(tree);

        Assert.Contains(extract.Calls, c => c.CalleeName == "real");
        Assert.DoesNotContain(extract.Calls, c => c.CalleeName == "foo");
        Assert.DoesNotContain(extract.Calls, c => c.CalleeName == "bar");
        Assert.DoesNotContain(extract.Calls, c => c.CalleeName == "baz");
    }

    [Fact]
    public void Extract_Broken_NoThrow()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From("function f( {\n"));
        StructuralExtract extract = _extractor.Extract(tree);
        Assert.NotNull(extract);
        Assert.Equal(SyntaxConfidence.Syntax, extract.Confidence);
    }

    [Fact]
    public void Register_WiresExtractor()
    {
        LanguagePackRegistry registry = new();
        JavaScriptRegistration.Register(registry);
        CstService cst = new(registry);
        ConcreteSyntaxTree? tree = cst.TryParse("a.js", SourceText.From("function f() { g(); }\nfunction g() {}"));
        Assert.NotNull(tree);
        StructuralExtract? extract = cst.TryExtract(tree!);
        Assert.NotNull(extract);
        Assert.Contains(extract!.Declarations, d => d.Name == "f");
        Assert.Contains(extract.Calls, c => c.CalleeName == "g");
    }
}
