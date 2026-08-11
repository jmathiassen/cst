namespace TgrDevelopments.Cst.Packs.Hcl;

internal enum HclTokenKind
{
    Eof = -1,
    Identifier = 1,
    StringLiteral,
    Number,
    KwResource,
    KwData,
    KwModule,
    KwVariable,
    KwOutput,
    KwProvider,
    KwLocals,
    KwTerraform,
    KwBackend,
    LBrace,
    RBrace,
    LParen,
    RParen,
    LBracket,
    RBracket,
    Eq,
    Other,
}
