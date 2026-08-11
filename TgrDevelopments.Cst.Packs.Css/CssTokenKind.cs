namespace TgrDevelopments.Cst.Packs.Css;

internal enum CssTokenKind
{
    Eof = -1,
    Ident = 1,
    StringLiteral,
    Hash,      // #id
    AtKeyword, // @media
    Number,
    LBrace,
    RBrace,
    LParen,
    RParen,
    LBracket,
    RBracket,
    Colon,
    Semicolon,
    Comma,
    Dot,
    Star,
    Gt,
    Plus,
    Tilde,
    Other,
}
