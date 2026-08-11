using System;

namespace TgrDevelopments.Cst.Packs.Ruby;

public enum RubyTokenKind
{
    Eof = -1,
    Identifier = 1,
    StringLiteral,
    SymbolLiteral,
    NumericLiteral,

    DotDot,
    DotDotDot,
    EqualEqual,
    NotEqual,
    LAngleEqual,
    RAngleEqual,
    AmpAmp,
    PipePipe,
    PlusPlus,
    MinusMinus,
    Arrow,
    ColonColon,

    LParen, RParen,
    LBrace, RBrace,
    LBracket, RBracket,
    Semicolon, Comma, Period, Colon, Equal,
    Plus, Minus, Star, Slash, Percent,
    Amp, Pipe, Caret, Tilde, Exclamation,
    LAngle, RAngle, Question, Underscore, At, Hash,

    KwModule, KwClass, KwDef, KwEnd, KwSelf, KwSuper,
    KwIf, KwThen, KwElse, KwElseIf, KwUnless, KwWhile, KwUntil,
    KwFor, KwIn, KwDo, KwNext, KwBreak, KwRedo, KwRetry,
    KwReturn, KwYield, KwNil, KwTrue, KwFalse,
    KwAnd, KwOr, KwNot, KwRescue, KwEnsure, KwBegin,
    KwCase, KwWhen, KwAlias, KwUndef,
}
