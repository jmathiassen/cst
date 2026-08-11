using System;

namespace TgrDevelopments.Cst.Packs.Rust;

public enum RustTokenKind
{
    Eof = -1,
    Identifier = 1,
    Lifetime,
    StringLiteral,
    RawStringLiteral,
    ByteStringLiteral,
    CharLiteral,
    NumericLiteral,

    ColonColon,
    Arrow,
    FatArrow,
    DotDot,
    DotDotDot,
    DotDotEqual,
    PlusPlus,
    MinusMinus,

    LParen, RParen,
    LBrace, RBrace,
    LBracket, RBracket,
    Semicolon, Comma, Period, Colon, Equal,
    Plus, Minus, Star, Slash, Percent,
    Amp, Pipe, Caret, Tilde, Hash, At, Exclamation,
    LAngle, RAngle, Question, Underscore,

    KwFn, KwPub, KwStruct, KwEnum, KwImpl, KwTrait, KwMod,
    KwUse, KwConst, KwStatic, KwLet, KwMut, KwAsync, KwUnsafe,
    KwExtern, KwReturn, KwWhere, KwFor, KwIn, KwIf, KwElse,
    KwWhile, KwLoop, KwMatch, KwRef, KwType, KwMove, KwDyn,
    KwSuper, KwSelf, KwCrate, KwTrue, KwFalse, KwUnion,
    KwMacroRules, KwBreak, KwContinue, KwAwait,
    Other,
}
