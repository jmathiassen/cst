using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Ruby;

internal sealed class RubyParser
{
    private const int MaxDepth = 64;
    private const int MaxOutlineNodes = 500;

    private readonly string _text;
    private readonly TokenReader _r;
    private readonly LineMap _map;
    private readonly List<ParseDiagnostic> _diagnostics;
    private readonly CancellationToken _ct;
    private int _depth;
    private int _nodeCount;
    private bool _partial;
    private bool _cancelled;

    public RubyParser(string text, IReadOnlyList<Token> tokens, LineMap map, List<ParseDiagnostic> diagnostics, CancellationToken ct)
    {
        _text = text;
        _r = new TokenReader(tokens, ct);
        _map = map;
        _diagnostics = diagnostics;
        _ct = ct;
    }

    public bool IsPartial => _partial;
    public bool IsCancelled => _cancelled;

    public List<SyntaxNode> Parse(out List<OutlineNode> outlines)
    {
        List<SyntaxNode> children = [];
        outlines = [];
        if (_r.Count <= 1)
            return children;
        try
        {
            ParseItems(children, outlines, 1, false);
        }
        catch (OperationCanceledException)
        {
            _cancelled = true;
        }
        return children;
    }

    private void ParseItems(List<SyntaxNode> parentChildren, List<OutlineNode> parentOutlines, int baseLevel, bool insideType, bool stopAtEnd = false)
    {
        while (!_r.IsEof)
        {
            _r.ThrowIfCancellationRequested();

            if (_r.Check((int)RubyTokenKind.KwEnd))
            {
                if (stopAtEnd) return;
                _r.Advance(); continue;
            }
            if (_r.Check((int)RubyTokenKind.KwElse) || _r.Check((int)RubyTokenKind.KwElseIf)
                || _r.Check((int)RubyTokenKind.KwWhen) || _r.Check((int)RubyTokenKind.KwEnsure)
                || _r.Check((int)RubyTokenKind.KwRescue))
            {
                if (stopAtEnd) return;
                _r.Advance(); continue;
            }
            if (_r.Check((int)RubyTokenKind.Semicolon))
            {
                _r.Advance(); continue;
            }

            if (_r.Check((int)RubyTokenKind.KwModule) || _r.Check((int)RubyTokenKind.KwClass))
            {
                TryParseModuleOrClass(parentChildren, parentOutlines, baseLevel);
                continue;
            }

            if (_r.Check((int)RubyTokenKind.KwDef))
            {
                TryParseDef(parentChildren, parentOutlines, baseLevel, insideType);
                continue;
            }

            SkipTrash();
        }
    }

    private bool TryParseModuleOrClass(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel)
    {
        int start = _r.Current.Start;
        bool isModule = _r.Check((int)RubyTokenKind.KwModule);
        string ok = isModule ? "module" : "class";
        _r.Advance();

        if (!_r.Check((int)RubyTokenKind.Identifier))
        {
            SkipToEnd();
            return true;
        }
        string name = _r.Current.Text ?? ok;
        TextSpan nameSpan = _r.Current.Span;
        _r.Advance();

        if (_r.Check((int)RubyTokenKind.LAngle) || _r.Check((int)RubyTokenKind.Period))
        {
            _r.Advance();
            if (_r.Check((int)RubyTokenKind.Identifier))
                _r.Advance();
        }

        if (baseLevel + 1 > MaxDepth)
        {
            _diagnostics.Add(new ParseDiagnostic("RUBY005", "Max depth exceeded.", new TextSpan(start, _r.Current.End - start), DiagnosticSeverity.Warning));
            SkipToEnd();
            AddNode(children, outlines, name, nameSpan, start, ok);
            return true;
        }

        _depth++;
        List<SyntaxNode> innerChildren = [];
        List<OutlineNode> innerOutlines = [];
        if (_nodeCount < MaxOutlineNodes)
            ParseItems(innerChildren, innerOutlines, baseLevel + 1, true, stopAtEnd: true);

        if (_r.Check((int)RubyTokenKind.KwEnd))
            _r.Advance();
        else
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("RUBY001", "Unclosed " + ok + " body.", new TextSpan(start, 0), DiagnosticSeverity.Error));
        }

        int end = _r.Peek(-1).End;
        CheckCaps(start, end);
        TextSpan span = new(start, end - start);
        children.Add(new SyntaxNode(ok + "_declaration", span, name, nameSpan, innerChildren.Count > 0 ? innerChildren : null));
        outlines.Add(MakeOutline(ok, name, span, baseLevel, innerOutlines.Count > 0 ? innerOutlines : null));
        _nodeCount++;
        _depth--;
        return true;
    }

    private bool TryParseDef(List<SyntaxNode> children, List<OutlineNode> outlines, int baseLevel, bool insideType)
    {
        int start = _r.Current.Start;
        _r.Advance();

        if (!_r.Check((int)RubyTokenKind.Identifier) && !_r.Check((int)RubyTokenKind.KwSelf))
        {
            string? opName = GetOperatorName();
            if (opName is null)
            {
                SkipToEnd();
                return true;
            }
            string opMethodName = opName;
            TextSpan opMethodSpan = _r.Current.Span;
            _r.Advance();
            if (_r.Check((int)RubyTokenKind.RBracket))
                _r.Advance();
            HandleDefBody(children, outlines, opMethodName, opMethodSpan, start, insideType);
            return true;
        }
        string name = _r.Current.Text ?? "def";
        TextSpan nameSpan = _r.Current.Span;
        _r.Advance();

        if (_r.Check((int)RubyTokenKind.Period))
        {
            _r.Advance();
            if (_r.Check((int)RubyTokenKind.Identifier) || _r.Check((int)RubyTokenKind.KwSelf))
            {
                name = _r.Current.Text ?? name;
                nameSpan = _r.Current.Span;
                _r.Advance();
            }
        }

        if (_r.Check((int)RubyTokenKind.DotDot) || _r.Check((int)RubyTokenKind.DotDotDot))
        {
            _r.Advance();
        }

        HandleDefBody(children, outlines, name, nameSpan, start, insideType);
        return true;
    }

    private void HandleDefBody(List<SyntaxNode> children, List<OutlineNode> outlines, string name, TextSpan nameSpan, int start, bool insideType)
    {
        if (_r.Check((int)RubyTokenKind.LParen))
        {
            if (!_r.TrySkipBalanced((int)RubyTokenKind.LParen, (int)RubyTokenKind.RParen))
            {
                _partial = true;
                _diagnostics.Add(new ParseDiagnostic("RUBY001", "Unclosed parameter list.", _r.Current.Span, DiagnosticSeverity.Error));
                SkipToEnd();
                AddNode(children, outlines, name, nameSpan, start, insideType ? "method" : "def");
                return;
            }
        }

        _depth++;
        List<SyntaxNode> innerChildren = [];
        List<OutlineNode> innerOutlines = [];
        if (_nodeCount < MaxOutlineNodes)
            ParseItems(innerChildren, innerOutlines, _depth, true, stopAtEnd: true);

        if (_r.Check((int)RubyTokenKind.KwEnd))
            _r.Advance();
        else
        {
            _partial = true;
            _diagnostics.Add(new ParseDiagnostic("RUBY001", "Unclosed def body.", new TextSpan(start, 0), DiagnosticSeverity.Error));
        }

        AddNode(children, outlines, name, nameSpan, start, insideType ? "method" : "def");
        _depth--;
    }

    private string? GetOperatorName()
    {
        RubyTokenKind k = (RubyTokenKind)_r.Current.Kind;
        return k switch
        {
            RubyTokenKind.EqualEqual => "==",
            RubyTokenKind.NotEqual => "!=",
            RubyTokenKind.LAngleEqual => "<=",
            RubyTokenKind.RAngleEqual => ">=",
            RubyTokenKind.LAngle => "<",
            RubyTokenKind.RAngle => ">",
            RubyTokenKind.Plus => "+",
            RubyTokenKind.Minus => "-",
            RubyTokenKind.Star => "*",
            RubyTokenKind.Slash => "/",
            RubyTokenKind.Percent => "%",
            RubyTokenKind.PipePipe => "||",
            RubyTokenKind.AmpAmp => "&&",
            RubyTokenKind.Pipe => "|",
            RubyTokenKind.Amp => "&",
            RubyTokenKind.Caret => "^",
            RubyTokenKind.Tilde => "~",
            RubyTokenKind.ColonColon => "::",
            RubyTokenKind.Arrow => "->",
            RubyTokenKind.LBracket => "[]",
            _ => null,
        };
    }

    private void AddNode(List<SyntaxNode> children, List<OutlineNode> outlines, string name, TextSpan nameSpan, int start, string ok)
    {
        int end = _r.Peek(-1).End;
        CheckCaps(start, end);
        TextSpan span = new(start, end - start);
        string nk = ok == "method" ? "method_declaration" : ok == "def" ? "function_declaration" : ok + "_declaration";
        children.Add(new SyntaxNode(nk, span, name, nameSpan));
        outlines.Add(MakeOutline(ok, name, span, _depth));
        _nodeCount++;
    }

    private void SkipToEnd()
    {
        int depth = 1;
        while (!_r.IsEof && depth > 0)
        {
            _r.ThrowIfCancellationRequested();
            if (_r.Check((int)RubyTokenKind.KwDef) || _r.Check((int)RubyTokenKind.KwClass)
                || _r.Check((int)RubyTokenKind.KwModule) || _r.Check((int)RubyTokenKind.KwDo))
                depth++;
            else if (_r.Check((int)RubyTokenKind.KwEnd))
                depth--;
            if (depth > 0)
                _r.Advance();
        }
        if (_r.Check((int)RubyTokenKind.KwEnd))
            _r.Advance();
    }

    private void SkipTrash()
    {
        while (!_r.IsEof)
        {
            RubyTokenKind k = (RubyTokenKind)_r.Current.Kind;
            if (k is RubyTokenKind.KwModule or RubyTokenKind.KwClass or RubyTokenKind.KwDef
                or RubyTokenKind.KwEnd or RubyTokenKind.KwElse or RubyTokenKind.KwElseIf
                or RubyTokenKind.KwWhen or RubyTokenKind.KwEnsure or RubyTokenKind.KwRescue
                or RubyTokenKind.Semicolon)
                return;
            _r.Advance();
        }
    }

    private void CheckCaps(int start, int end)
    {
        if (_depth >= MaxDepth)
            _diagnostics.Add(new ParseDiagnostic("RUBY005", "Max depth exceeded.", new TextSpan(start, end - start), DiagnosticSeverity.Warning));
        if (_nodeCount >= MaxOutlineNodes)
            _diagnostics.Add(new ParseDiagnostic("RUBY006", "Max outline nodes exceeded.", new TextSpan(start, end - start), DiagnosticSeverity.Warning));
    }

    private OutlineNode MakeOutline(string kind, string label, TextSpan span, int level, IReadOnlyList<OutlineNode>? children = null)
    {
        LinePositionSpan lp = _map.GetLinePositionSpan(span);
        LinePositionSpan nav = new(new LinePosition(lp.Start.Line, 1), new LinePosition(lp.End.Line, 1));
        return new OutlineNode(kind, label, span, nav, level, children);
    }
}
