using System;

namespace TgrDevelopments.Cst.Packs.Kotlin;

public enum KotlinTokenKind
{
    Eof = -1,
    Identifier = 1,
    StringLiteral,
    RawStringLiteral,
    CharLiteral,
    NumericLiteral,

    Arrow,
    ColonColon,
    DotDot,
    EqualEqual,
    NotEqual,
    LAngleEqual,
    RAngleEqual,
    AmpAmp,
    PipePipe,
    PlusPlus,
    MinusMinus,
    ArrowRight,

    LParen, RParen,
    LBrace, RBrace,
    LBracket, RBracket,
    Semicolon, Comma, Period, Colon, Equal,
    Plus, Minus, Star, Slash, Percent,
    Amp, Pipe, Caret, Tilde, At, Exclamation,
    LAngle, RAngle, Question, Underscore,

    KwPackage, KwImport, KwClass, KwInterface, KwObject, KwEnum,
    KwFun, KwVal, KwVar, KwData, KwSealed, KwOpen, KwAbstract,
    KwOverride, KwPrivate, KwProtected, KwInternal, KwPublic,
    KwCompanion, KwInit, KwConstructor, KwBy, KwLazy, KwLateInit,
    KwInline, KwSuspend, KwOperator, KwInfix, KwTailRec, KwExternal,
    KwAnnotation, KwInner, KwReturn, KwIf, KwElse, KwFor, KwWhile,
    KwWhen, KwBreak, KwContinue, KwNull, KwTrue, KwFalse,
    KwIs, KwNot, KwIn, KwTypeAlias, KwWhere,
}
