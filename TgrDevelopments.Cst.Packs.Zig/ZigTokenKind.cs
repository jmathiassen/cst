using System;

namespace TgrDevelopments.Cst.Packs.Zig;

public enum ZigTokenKind
{
    Eof = -1,
    Identifier = 1,
    StringLiteral,
    CharLiteral,
    NumericLiteral,

    Arrow,
    FatArrow,
    DotDot,
    DotDotEqual,
    EqualEqual,
    NotEqual,
    AmpAmp,
    PipePipe,
    PlusPlus,
    MinusMinus,
    LAngleEqual,
    RAngleEqual,

    LParen, RParen,
    LBrace, RBrace,
    LBracket, RBracket,
    Semicolon, Comma, Period, Colon, Equal,
    Plus, Minus, Star, Slash, Percent,
    Amp, Pipe, Caret, Tilde, At, Exclamation,
    LAngle, RAngle, Question, Underscore,

    KwFn, KwPub, KwExport, KwConst, KwVar,
    KwStruct, KwEnum, KwUnion, KwTest,
    KwComptime, KwUsingNamespace, KwReturn,
    KwIf, KwElse, KwFor, KwWhile, KwBreak,
    KwContinue, KwDefer, KwErrDefer, KwUnreachable,
    KwVoid, KwNull, KwTrue, KwFalse,
    KwAnyError, KwAnyType, KwType, KwNoAlias,
}
