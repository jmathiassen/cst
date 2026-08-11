using System;

namespace TgrDevelopments.Cst.Packs.Java;

public enum JavaTokenKind
{
    Eof = -1,
    Identifier = 1,
    StringLiteral,
    TextBlock,
    CharLiteral,
    NumericLiteral,

    Arrow,
    ColonColon,
    DotDotDot,
    EqualEqual,
    NotEqual,
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
    Amp, Pipe, Caret, Tilde, At, Exclamation,
    LAngle, RAngle, Question,

    KwPackage, KwImport, KwClass, KwInterface, KwEnum, KwRecord,
    KwExtends, KwImplements, KwPublic, KwPrivate, KwProtected,
    KwStatic, KwAbstract, KwFinal, KwVoid, KwReturn, KwIf, KwElse,
    KwFor, KwWhile, KwDo, KwSwitch, KwCase, KwDefault, KwBreak,
    KwContinue, KwNew, KwThis, KwSuper, KwNull, KwTrue, KwFalse,
    KwInstanceOf, KwThrows, KwThrow, KwTry, KwCatch, KwFinally,
    KwNative, KwSynchronized, KwVolatile, KwTransient,
    KwStrictFp, KwVar, KwYield, KwSealed, KwPermits,
}
