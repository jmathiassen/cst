using System.Linq;

using Xunit;

using TgrDevelopments.Cst;
using TgrDevelopments.Cst.Packs.Xml;

namespace TgrDevelopments.Cst.Packs.Xml.Tests;

public class XmlExtractorTests
{
    private readonly XmlLanguagePack _pack = new();
    private readonly XmlStructuralExtractor _extractor = new();

    [Fact]
    public void Extract_Empty_Empty()
    {
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(""));
        StructuralExtract extract = _extractor.Extract(tree);
        Assert.Empty(extract.Edges);
        Assert.Empty(extract.Declarations);
    }

    [Fact]
    public void Extract_Csproj_PackageAndProjectRefs()
    {
        string text = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="xunit" Version="2.9.3" />
                <ProjectReference Include="..\Core\Core.csproj" />
              </ItemGroup>
            </Project>
            """;
        ConcreteSyntaxTree tree = _pack.Parse(SourceText.From(text, "App.csproj"));
        StructuralExtract extract = _extractor.Extract(tree);

        Assert.Contains(extract.Edges, e => e.Kind == "package_reference" && e.To == "xunit");
        Assert.Contains(extract.Edges, e => e.Kind == "project_reference" && e.To.Contains("Core.csproj"));
        Assert.Contains(extract.Declarations, d => d.Kind == "target_framework" && d.Name == "net10.0");
        Assert.Equal(SyntaxQuality.Structural, extract.Quality);
        Assert.Equal(SyntaxConfidence.Syntax, extract.Confidence);
    }
}
