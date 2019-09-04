﻿namespace CssParser.Lexer
{
    public enum TokenType
    {
        Comment,
        Ident,
        Function,
        AtKeyword,
        Hash,
        String,
        BadString,
        Url,
        BadUrl,
        Delim,
        Number,
        Percentage,
        Dimension,
        Whitespace,
        Cdo,
        Cdc,
        Colon,
        Semicolon,
        Comma,
        LeftSquareBracket,
        RightSquareBracket,
        LeftBracket,
        RightBracket,
        LeftBrace,
        RightBrace,
        EOF
    }
}
