using System;

namespace TgrDevelopments.Cst.Packs.Lua;

public enum LuaTokenKind
{
    Eof = -1,
    Identifier = 1,
    StringLiteral,
    LongStringLiteral,
    NumericLiteral,

    DotDot,
    DotDotDot,
    EqualEqual,
    NotEqual,
    LAngleEqual,
    RAngleEqual,
    PlusPlus,
    MinusMinus,

    LParen, RParen,
    LBrace, RBrace,
    LBracket, RBracket,
    Semicolon, Comma, Period, Colon, Equal,
    Plus, Minus, Star, Slash, Percent,
    Amp, Pipe, Caret, Tilde, Exclamation,
    LAngle, RAngle, Hash, Question,

    KwFunction, KwLocal, KwEnd, KwIf, KwThen, KwElse,
    KwElseIf, KwFor, KwWhile, KwDo, KwRepeat, KwUntil,
    KwReturn, KwBreak, KwGoto, KwIn, KwNil,
    KwTrue, KwFalse, KwAnd, KwOr, KwNot,
}
