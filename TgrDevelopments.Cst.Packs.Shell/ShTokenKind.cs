namespace TgrDevelopments.Cst.Packs.Shell;

internal enum ShTokenKind
{
    Eof = -1,
    Identifier = 1,
    NumericLiteral,
    StringLiteral,
    KwFunction,
    LParen,
    RParen,
    LBrace,
    RBrace,
    LBracket,
    RBracket,
    Semicolon,
    Newline,
    Other,
}
