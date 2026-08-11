using System;

namespace TgrDevelopments.Cst.Packs.Php;

public enum PhpTokenKind
{
    Eof = -1,
    Identifier = 1,
    StringLiteral,
    HeredocLiteral,
    NumericLiteral,

    Arrow,
    ColonColon,
    DotDot,
    EqualEqual,
    NotEqual,
    EqualEqualEqual,
    NotEqualEqual,
    LAngleEqual,
    RAngleEqual,
    AmpAmp,
    PipePipe,
    PlusPlus,
    MinusMinus,

    LParen, RParen,
    LBrace, RBrace,
    LBracket, RBracket,
    Semicolon, Comma, Period, Colon, Equal,
    Plus, Minus, Star, Slash, Percent,
    Amp, Pipe, Caret, Tilde, Exclamation,
    LAngle, RAngle, At, Question, Hash,

    KwNamespace, KwClass, KwTrait, KwInterface, KwEnum,
    KwFunction, KwPublic, KwPrivate, KwProtected, KwStatic,
    KwAbstract, KwFinal, KwReadonly, KwConst, KwVar,
    KwReturn, KwIf, KwElse, KwElseIf, KwFor, KwWhile, KwDo,
    KwSwitch, KwCase, KwBreak, KwContinue,
    KwTry, KwCatch, KwFinally, KwThrow,
    KwNull, KwTrue, KwFalse,
    KwMatch, KwFn, KwUse, KwAs, KwInsteadOf,
    KwExtends, KwImplements,
    Backslash,
}
