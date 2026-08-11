using System;

namespace TgrDevelopments.Cst.Packs.Swift;

public enum SwiftTokenKind
{
    Eof = -1,
    Identifier = 1,
    StringLiteral,
    MultiLineStringLiteral,
    NumericLiteral,

    Arrow,
    ColonColon,
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
    QuestionQuestion,

    LParen, RParen,
    LBrace, RBrace,
    LBracket, RBracket,
    Semicolon, Comma, Period, Colon, Equal,
    Plus, Minus, Star, Slash, Percent,
    Amp, Pipe, Caret, Tilde, At, Exclamation,
    LAngle, RAngle, Question, Underscore, Hash,

    KwClass, KwStruct, KwEnum, KwProtocol, KwExtension,
    KwFunc, KwInit, KwDeinit, KwLet, KwVar,
    KwPublic, KwPrivate, KwInternal, KwFilePrivate, KwOpen,
    KwStatic, KwOverride, KwThrows, KwRethrows, KwAsync, KwAwait,
    KwInOut, KwMutating, KwNonMutating, KwConvenience, KwRequired,
    KwWeak, KwUnowned, KwLazy, KwDynamic, KwFinal,
    KwReturn, KwIf, KwElse, KwFor, KwWhile, KwDo,
    KwSwitch, KwCase, KwBreak, KwContinue,
    KwTry, KwCatch, KwThrow,
    KwNil, KwTrue, KwFalse, KwSelf, KwSuper,
    KwTypeAlias, KwWhere,
}
