# TgrDevelopments.Cst

Concrete syntax tree library: multi-format parse → tree → navigable outline (1-based From/To lines).

Namespace: `TgrDevelopments.Cst` · Target: .NET 10 · Specs: `../../specs/cst/`

## Implemented packs

All **40 language packs** are graded `Structural` — the production bar. No packs remain at `Preview` or `Experimental`.

| Phase | Packs |
|-------|-------|
| P0 | Core (`LineMap`, registry, `CstService`) |
| P1 | Markdown, Csv, Json |
| P2 | Xml, Yaml, Ini, Toml, Html, Sln |
| P3 | JavaScript, TypeScript, CSharpPreview |
| P7 | C, Cpp, Rust, Odin, Go, Zig |
| P8 | Python, Shell, PowerShell, Sql |
| P9 | Css, Dockerfile, Env, Proto, Graphql, Hcl |
| P10 | Latex, Rst, Vb, FSharp |
| P11 | Java, Kotlin, Swift, Ruby, Lua, Php, Dart, Perl |

| P4 | Extractors: JS/TS (required), csharp-preview, xml, sln, markdown |

Not implemented: P5 Roslyn adapter, P6 daemon.

## Usage

```csharp
LanguagePackRegistry registry = new();
MarkdownRegistration.Register(registry);
// …register other packs as needed

CstService cst = new(registry);
ConcreteSyntaxTree? tree = cst.TryParse("readme.md", SourceText.From(text, "readme.md"));
IReadOnlyList<OutlineNode> outline = cst.GetOutline(tree!);
```

## Build / test

```bash
dotnet test TgrDevelopments.Cst.sln
```
