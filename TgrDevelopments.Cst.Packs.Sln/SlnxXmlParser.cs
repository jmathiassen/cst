using System;
using System.Collections.Generic;
using System.Threading;

using TgrDevelopments.Cst;

namespace TgrDevelopments.Cst.Packs.Sln;

/// <summary>
/// Parses <c>.slnx</c> files — the VS 17.10+ XML solution format. Closed element set:
/// <c>Solution</c>, <c>Folder</c>, <c>Project</c> with <c>Path</c>/<c>Name</c> attributes.
/// Unknown elements (e.g. <c>ProjectConfiguration</c>) are tracked for nesting depth but
/// produce no node; malformed XML yields <c>SLN004</c> (Error; partial) and never throws.
/// </summary>
internal sealed class SlnxXmlParser
{
    private readonly string _text;
    private readonly LineMap _map;
    private readonly List<ParseDiagnostic> _diagnostics;
    private readonly CancellationToken _ct;
    private int _pos;
    private bool _hasErrors;

    /// <summary>Creates a parser over the given text.</summary>
    public SlnxXmlParser(string text, LineMap map, List<ParseDiagnostic> diagnostics, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(diagnostics);
        _text = text;
        _map = map;
        _diagnostics = diagnostics;
        _ct = ct;
    }

    /// <summary>
    /// Scans the XML, filling top-level child nodes and outlines. Emits <c>SLN004</c> (Error)
    /// and sets <paramref name="isPartial"/> on malformed XML; never throws.
    /// </summary>
    public void Parse(List<SyntaxNode> children, List<OutlineNode> outlines, ref bool isPartial)
    {
        ArgumentNullException.ThrowIfNull(children);
        ArgumentNullException.ThrowIfNull(outlines);

        Stack<OpenElement> stack = new();

        while (_pos < _text.Length)
        {
            _ct.ThrowIfCancellationRequested();
            SkipUntil('<');
            if (_pos >= _text.Length)
                break;

            if (_pos + 1 >= _text.Length)
            {
                AddError(_pos, "Unexpected end of input after '<'.");
                break;
            }

            char next = _text[_pos + 1];
            if (next == '?')
            {
                SkipTo('?', '>');
            }
            else if (next == '!')
            {
                if (StartsWithAt(_pos, "<!--"))
                    SkipTo('-', '-', '>');
                else if (StartsWithAt(_pos, "<![CDATA["))
                    SkipTo(']', ']', '>');
                else
                    SkipTo('>');
            }
            else if (next == '/')
            {
                HandleCloseTag(stack, children, outlines);
            }
            else
            {
                HandleOpenTag(stack, children, outlines);
            }
        }

        while (stack.Count > 0)
        {
            OpenElement el = stack.Pop();
            AddError(el.TagStart, $"Unterminated element <{el.Name}>; expected </{el.Name}>.");
            FinalizeElement(el, children, outlines, _text.Length);
        }

        isPartial = isPartial || _hasErrors;
    }

    private void HandleOpenTag(Stack<OpenElement> stack, List<SyntaxNode> children, List<OutlineNode> outlines)
    {
        int tagStart = _pos;
        _pos++;
        string name = ReadName();
        if (name.Length == 0)
        {
            AddError(tagStart, "Malformed element name in .slnx XML.");
            SkipTo('>');
            return;
        }

        string? nameAttr = null;
        string? pathAttr = null;
        TextSpan? nameAttrSpan = null;
        TextSpan? pathAttrSpan = null;
        bool selfClosing = false;
        bool malformed = false;

        while (!malformed)
        {
            SkipWhitespace();
            if (_pos >= _text.Length)
            {
                malformed = true;
                break;
            }

            char c = _text[_pos];
            if (c == '/')
            {
                _pos++;
                if (_pos < _text.Length && _text[_pos] == '>')
                {
                    _pos++;
                    selfClosing = true;
                }
                else
                {
                    malformed = true;
                }
                break;
            }
            if (c == '>')
            {
                _pos++;
                break;
            }

            string attrName = ReadName();
            if (attrName.Length == 0)
            {
                malformed = true;
                break;
            }

            SkipWhitespace();
            if (_pos >= _text.Length || _text[_pos] != '=')
            {
                malformed = true;
                break;
            }
            _pos++;
            SkipWhitespace();
            if (_pos >= _text.Length || (_text[_pos] != '"' && _text[_pos] != '\''))
            {
                malformed = true;
                break;
            }

            char quote = _text[_pos++];
            int valueStart = _pos;
            while (_pos < _text.Length && _text[_pos] != quote)
                _pos++;
            if (_pos >= _text.Length)
            {
                malformed = true;
                break;
            }

            string value = _text.Substring(valueStart, _pos - valueStart);
            _pos++;

            if (string.Equals(attrName, "Name", StringComparison.OrdinalIgnoreCase))
            {
                nameAttr = value;
                nameAttrSpan = new TextSpan(valueStart, value.Length);
            }
            else if (string.Equals(attrName, "Path", StringComparison.OrdinalIgnoreCase))
            {
                pathAttr = value;
                pathAttrSpan = new TextSpan(valueStart, value.Length);
            }
        }

        if (malformed)
        {
            AddError(tagStart, "Malformed attribute or tag in .slnx XML.");
            SkipTo('>');
            return;
        }

        OpenElement el = new(name, tagStart, stack.Count, nameAttr, pathAttr, nameAttrSpan, pathAttrSpan);
        if (selfClosing)
        {
            if (stack.Count > 0)
            {
                OpenElement parent = stack.Peek();
                FinalizeElement(el, parent.Children, parent.Outlines, _pos);
            }
            else
            {
                FinalizeElement(el, children, outlines, _pos);
            }
        }
        else
        {
            stack.Push(el);
        }
    }

    private void HandleCloseTag(Stack<OpenElement> stack, List<SyntaxNode> children, List<OutlineNode> outlines)
    {
        int tagStart = _pos;
        _pos += 2;
        string name = ReadName();
        if (name.Length == 0)
        {
            AddError(tagStart, "Malformed closing tag in .slnx XML.");
            SkipTo('>');
            return;
        }

        SkipTo('>');
        int closeEnd = _pos;

        if (stack.Count == 0)
        {
            AddError(tagStart, $"Unexpected closing tag </{name}> in .slnx XML.");
            return;
        }

        OpenElement top = stack.Peek();
        if (!string.Equals(top.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            AddError(tagStart, $"Mismatched closing tag </{name}>; expected </{top.Name}>.");
            return;
        }

        stack.Pop();
        if (stack.Count > 0)
        {
            OpenElement parent = stack.Peek();
            FinalizeElement(top, parent.Children, parent.Outlines, closeEnd);
        }
        else
        {
            FinalizeElement(top, children, outlines, closeEnd);
        }
    }

    private void FinalizeElement(
        OpenElement el,
        List<SyntaxNode> parentNodes,
        List<OutlineNode> parentOutlines,
        int endOffset)
    {
        bool isFolder = string.Equals(el.Name, "Folder", StringComparison.OrdinalIgnoreCase);
        bool isProject = string.Equals(el.Name, "Project", StringComparison.OrdinalIgnoreCase);
        if (!isFolder && !isProject)
        {
            parentNodes.AddRange(el.Children);
            parentOutlines.AddRange(el.Outlines);
            return;
        }

        string kind = isFolder ? "folder" : "project";
        string nodeName = el.NameAttr ?? PathFileName(el.PathAttr) ?? kind;
        TextSpan span = new(el.TagStart, endOffset - el.TagStart);
        TextSpan? nameSpan = el.NameAttrSpan ?? PathNameSpan(el);
        SyntaxNode node = new(kind, span, nodeName, nameSpan, el.Children);
        parentNodes.Add(node);

        int fromLine = _map.GetLineNumber(span.Start);
        int toLine = endOffset > span.Start ? _map.GetLineNumber(endOffset - 1) : fromLine;
        OutlineNode outline = TextUtil.Outline(kind, nodeName, _map, fromLine, toLine, el.Depth, el.Outlines);
        parentOutlines.Add(outline);
    }

    private void AddError(int offset, string message)
    {
        _hasErrors = true;
        _diagnostics.Add(new ParseDiagnostic(
            "SLN004",
            message,
            new TextSpan(offset, 0),
            DiagnosticSeverity.Error));
    }

    private static string? PathFileName(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        int slash = path.LastIndexOf('/');
        int bslash = path.LastIndexOf('\\');
        int cut = Math.Max(slash, bslash) + 1;
        string name = path.Substring(cut);
        return name.Length > 0 ? name : null;
    }

    private static TextSpan? PathNameSpan(OpenElement el)
    {
        if (el.PathAttrSpan is not TextSpan p || el.PathAttr is null)
            return null;
        int slash = el.PathAttr.LastIndexOf('/');
        int bslash = el.PathAttr.LastIndexOf('\\');
        int cut = Math.Max(slash, bslash) + 1;
        return new TextSpan(p.Start + cut, p.Length - cut);
    }

    private string ReadName()
    {
        int start = _pos;
        while (_pos < _text.Length)
        {
            char c = _text[_pos];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ':' || c == '.')
                _pos++;
            else
                break;
        }
        return _text.Substring(start, _pos - start);
    }

    private void SkipWhitespace()
    {
        while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
            _pos++;
    }

    private void SkipUntil(char target)
    {
        while (_pos < _text.Length && _text[_pos] != target)
            _pos++;
    }

    private void SkipTo(char target)
    {
        while (_pos < _text.Length && _text[_pos] != target)
            _pos++;
        if (_pos < _text.Length)
            _pos++;
    }

    private void SkipTo(char a, char b)
    {
        _pos += 2;
        while (_pos < _text.Length - 1 && !(_text[_pos] == a && _text[_pos + 1] == b))
            _pos++;
        _pos = Math.Min(_pos + 2, _text.Length);
    }

    private void SkipTo(char a, char b, char c)
    {
        _pos += 3;
        while (_pos < _text.Length - 2 && !(_text[_pos] == a && _text[_pos + 1] == b && _text[_pos + 2] == c))
            _pos++;
        _pos = Math.Min(_pos + 3, _text.Length);
    }

    private bool StartsWithAt(int offset, string value)
    {
        if (offset + value.Length > _text.Length)
            return false;
        for (int i = 0; i < value.Length; i++)
        {
            if (_text[offset + i] != value[i])
                return false;
        }
        return true;
    }

    private sealed class OpenElement
    {
        public string Name { get; }
        public int TagStart { get; }
        public int Depth { get; }
        public string? NameAttr { get; }
        public string? PathAttr { get; }
        public TextSpan? NameAttrSpan { get; }
        public TextSpan? PathAttrSpan { get; }
        public List<SyntaxNode> Children { get; } = [];
        public List<OutlineNode> Outlines { get; } = [];

        public OpenElement(
            string name,
            int tagStart,
            int depth,
            string? nameAttr,
            string? pathAttr,
            TextSpan? nameAttrSpan,
            TextSpan? pathAttrSpan)
        {
            ArgumentNullException.ThrowIfNull(name);
            Name = name;
            TagStart = tagStart;
            Depth = depth;
            NameAttr = nameAttr;
            PathAttr = pathAttr;
            NameAttrSpan = nameAttrSpan;
            PathAttrSpan = pathAttrSpan;
        }
    }
}
