namespace TgrDevelopments.Cst.Packs.PowerShell;

internal enum PsTokenKind
{
    Eof = -1,
    Identifier = 1,
    NumericLiteral,
    StringLiteral,
    KwFunction,
    KwFilter,
    KwClass,
    KwEnum,
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
