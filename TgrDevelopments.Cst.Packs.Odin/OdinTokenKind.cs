using System;

namespace TgrDevelopments.Cst.Packs.Odin;

public enum OdinTokenKind
{
    Eof = -1,
    Identifier = 1,
    StringLiteral,
    RawStringLiteral,
    CharLiteral,
    NumericLiteral,

    ColonColon,
    Arrow,
    FatArrow,
    DotDot,
    PlusPlus,
    MinusMinus,

    LParen, RParen,
    LBrace, RBrace,
    LBracket, RBracket,
    Semicolon, Comma, Period, Colon, Equal,
    Plus, Minus, Star, Slash, Percent,
    Amp, Pipe, Caret, Tilde, Hash, At, Exclamation,
    LAngle, RAngle, Question, Underscore,

    KwPackage, KwProc, KwStruct, KwEnum, KwUnion, KwImport,
    KwForeign, KwUsing, KwDefer, KwWhen, KwIf, KwElse, KwFor,
    KwReturn, KwBreak, KwContinue, KwIn, KwNot, KwOr, KwAnd,
    KwTrue, KwFalse, KwNil, KwMake, KwNew, KwDelete,
}
