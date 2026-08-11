using System.Linq;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.CSharpPreview;

namespace TgrDevelopments.Cst.Packs.CSharpPreview.Tests;

public class CSharpPreviewExtractorTests
{
    private readonly CSharpPreviewLanguagePack _pack = new();
    private readonly CSharpPreviewStructuralExtractor _extractor = new();

    [Fact]
    public void Extract_Empty_Empty()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(""));
        StructuralExtract extract = _extractor.Extract(tree);
        Assert.Empty(extract.Declarations);
    }

    [Fact]
    public void Extract_Happy_NamespaceClassMethod()
    {
        string text = """
            namespace App;

            public class Engine
            {
                public void Run()
                {
                    Helper();
                }
            }
            """;
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(text));
        StructuralExtract extract = _extractor.Extract(tree);

        Assert.Contains(extract.Declarations, d => d.Name == "App" && d.Kind == "namespace");
        Assert.Contains(extract.Declarations, d => d.Name == "Engine" && d.Kind == "class");
        Assert.Contains(extract.Declarations, d => d.Name == "Run" && d.Kind == "method");
        Assert.Contains(extract.Calls, c => c.CalleeName == "Helper");
        Assert.Equal(SyntaxConfidence.Syntax, extract.Confidence);
    }
}
