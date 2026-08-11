namespace TgrDevelopments.Cst.Packs.Proto;

internal enum ProtoTokenKind
{
    Eof = -1,
    Identifier = 1,
    StringLiteral = 3,
    KwPackage = 10,
    KwMessage = 11,
    KwEnum = 12,
    KwService = 13,
    KwRpc = 14,
    LBrace = 20,
    RBrace = 21,
    LParen = 22,
    RParen = 23,
    Semicolon = 24,
    Dot = 25,
    Other = 99,
}
