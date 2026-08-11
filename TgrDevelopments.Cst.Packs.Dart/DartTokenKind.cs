using System;

namespace TgrDevelopments.Cst.Packs.Dart;

public enum DartTokenKind
{
    Eof = -1,
    Identifier = 1,
    StringLiteral,
    RawStringLiteral,
    MultiLineStringLiteral,
    CharLiteral,
    NumericLiteral,

    Arrow,
    ColonColon,
    DotDot,
    DotDotQuestion,
    EqualEqual,
    NotEqual,
    LAngleEqual,
    RAngleEqual,
    AmpAmp,
    PipePipe,
    PlusPlus,
    MinusMinus,
    QuestionQuestion,
    ArrowRight,

    LParen, RParen,
    LBrace, RBrace,
    LBracket, RBracket,
    Semicolon, Comma, Period, Colon, Equal,
    Plus, Minus, Star, Slash, Percent,
    Amp, Pipe, Caret, Tilde, At, Exclamation,
    LAngle, RAngle, Question, Underscore, Hash,

    KwClass, KwMixin, KwEnum, KwExtends, KwWith, KwImplements,
    KwAbstract, KwBase, KwSealed, KwFinal, KwConst, KwFactory,
    KwNew, KwReturn, KwIf, KwElse, KwFor, KwWhile, KwDo,
    KwSwitch, KwCase, KwDefault, KwBreak, KwContinue,
    KwTry, KwCatch, KwFinally, KwThrow, KwRethrow,
    KwVar, KwFinal_, KwLate, KwRequired, KwVoid,
    KwNull, KwTrue, KwFalse, KwIs, KwAs, KwShow, KwHide,
    KwExport, KwLibrary, KwPart, KwImport, KwTypedef,
}
