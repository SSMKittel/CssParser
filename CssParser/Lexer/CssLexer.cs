using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using System.Diagnostics;

namespace CssParser.Lexer
{
    public class CssLexer : IDisposable
    {
        private const int EOF = Preprocessor.EOF;
        private const int SurrogateLow = 0xd800;
        private const int SurrogateHigh = 0xdfff;
        private const int UnicodeMax = 0x10FFFF;

        private Preprocessor _css;
        private uint _line;
        private uint _position;
        private uint _tokenStartLine;
        private uint _tokenStart;

        private StringBuilder _representation;

        private Stack<int> _back;

        public CssLexer(TextReader css)
        {
            _css = new Preprocessor(css);
            _line = 1;
            _position = 0;
            _representation = new StringBuilder();
            _back = new Stack<int>(5);
        }

        public async Task<Token> Next()
        {
            _tokenStartLine = _line;
            _tokenStart = _position;
            _representation.Clear();

            int current = await Advance();
            if (current == EOF)
            {
                return Eof();
            }
            else if (current == '/')
            {
                current = await Advance();
                if (current == EOF)
                {
                    return Token(TokenType.Delim, "/");
                }
                else if (current != '*')
                {
                    PushBack(current);
                    return Token(TokenType.Delim, "/");
                }

                return await ReadComment();
            }
            else if (IsWhitespace(current))
            {
                PushBack(current);
                return await ReadWhitespace();
            }
            else if (current == '"' || current == '\'')
            {
                return await ReadString(current);
            }
            else if (current == '#')
            {
                var next3 = await Peek3();
                if (IsNameCodePoint(next3[0])
                    || (next3[0] == '\\' && next3[1] != EOF && next3[1] != '\n'))
                {
                    bool idType = IsIdent(next3);
                    string name = await ReadName();
                    var tok = Token(TokenType.Hash, name);
                    tok.IsHashId = idType;
                    return tok;
                }
                else
                {
                    return Token(TokenType.Delim, "#");
                }
            }
            else if (current == '+')
            {
                PushBack(current);
                var next3 = await Peek3();
                if (IsNumber(next3))
                {
                    return await ReadNumber();
                }
                await Advance();
                return Token(TokenType.Delim, "+");
            }
            else if (current == '-')
            {
                PushBack(current);
                var next3 = await Peek3();
                if (IsNumber(next3))
                {
                    return await ReadNumber();
                }
                else if (IsCdc(next3))
                {
                    await Advance();
                    await Advance();
                    await Advance();
                    return Token(TokenType.Cdc, "-->");
                }
                else if (IsIdent(next3))
                {
                    return await ReadIdent();
                }
                await Advance();
                return Token(TokenType.Delim, "-");
            }
            else if (current == '.')
            {
                int next = await Peek();
                if (next == EOF)
                {
                    return Token(TokenType.Delim, ".");
                }
                else if (IsDigit(next))
                {
                    PushBack(current);
                    return await ReadNumber();
                }
                else
                {
                    return Token(TokenType.Delim, ".");
                }
            }
            else if (IsDigit(current))
            {
                PushBack(current);
                return await ReadNumber();
            }
            else if (current == ',')
            {
                return Token(TokenType.Comma, ",");
            }
            else if (current == ':')
            {
                return Token(TokenType.Colon, ":");
            }
            else if (current == ';')
            {
                return Token(TokenType.Semicolon, ";");
            }
            else if (current == '<')
            {
                var next3 = await Peek3();
                if (IsCdo(next3))
                {
                    await Advance();
                    await Advance();
                    await Advance();
                    return Token(TokenType.Cdo, "<!--");
                }
                else
                {
                    return Token(TokenType.Delim, "<");
                }
            }
            else if (current == '@')
            {
                var next3 = await Peek3();
                if (IsIdent(next3))
                {
                    var name = await ReadName();
                    return Token(TokenType.AtKeyword, name);
                }
                else
                {
                    return Token(TokenType.Delim, "@");
                }
            }
            else if (current == '\\')
            {
                var next = await Peek();
                if (next == EOF || next == '\n')
                {
                    // parse error
                    return Token(TokenType.Delim, "\\");
                }
                else
                {
                    PushBack(current);
                    return await ReadIdent();
                }
            }
            else if (current == '[')
            {
                return Token(TokenType.LeftSquareBracket, "[");
            }
            else if (current == ']')
            {
                return Token(TokenType.RightSquareBracket, "]");
            }
            else if (current == '(')
            {
                return Token(TokenType.LeftBracket, "(");
            }
            else if (current == ')')
            {
                return Token(TokenType.RightBracket, ")");
            }
            else if (current == '{')
            {
                return Token(TokenType.LeftBrace, "{");
            }
            else if (current == '}')
            {
                return Token(TokenType.RightBrace, "}");
            }
            else if (IsNameStartCodePoint(current))
            {
                PushBack(current);
                return await ReadIdent();
            }
            else
            {
                return Token(TokenType.Delim, current.ToString());
            }
        }

        private async Task<string> ConsumeWhitespace()
        {
            var sb = new StringBuilder();
            while (true)
            {
                int current = await Advance();
                if (current == EOF)
                {
                    return sb.ToString();
                }
                else if (!IsWhitespace(current))
                {
                    PushBack(current);
                    return sb.ToString();
                }
                sb.AppendCodePoint(current);
            }
        }

        private async Task<Token> ReadIdent()
        {
            string name = await ReadName();
            int c = await Advance();
            bool nextBracket = c == '(';
            if (name.Equals("url", StringComparison.OrdinalIgnoreCase) && nextBracket)
            {
                string white = await ConsumeWhitespace();
                int next = await Peek();
                if (next == '"' || next == '\'')
                {
                    // Treat it like a normal function, preserve whitespace token
                    for (int i = white.Length - 1; i >= 0; i--)
                    {
                        PushBack(white[i]);
                    }
                    return Token(TokenType.Function, name);
                }
                return await ReadUrl();
            }
            else if (nextBracket)
            {
               return Token(TokenType.Function, name);
            }
            else
            {
                PushBack(c);
                return Token(TokenType.Ident, name);
            }
        }

        private async Task<Token> ReadUrl()
        {
            var b = new StringBuilder();
            while(true)
            {
                int c = await Advance();
                if (c == EOF)
                {
                    // parse error
                    return Token(TokenType.Url, b.ToString());
                }
                else if (IsWhitespace(c))
                {
                    string ws = await ConsumeWhitespace();
                    int next = await Peek();
                    if (next == ')')
                    {
                        await Advance();
                        return Token(TokenType.Url, b.ToString());
                    }
                    else
                    {
                        // parse error
                        b.AppendCodePoint(c).Append(ws);
                        return await ReadBadUrl(b);
                    }
                }
                else if (c == ')')
                {
                    return Token(TokenType.Url, b.ToString());
                }
                else if (c == '\'' || c == '"' || c == '(' || IsNonPrinting(c))
                {
                    // parse error
                    b.AppendCodePoint(c);
                    return await ReadBadUrl(b);
                }
                else if (c == '\\')
                {
                    var n = await Peek();
                    if (n == EOF || n == '\n')
                    {
                        // parse error
                        PushBack(c);
                        return await ReadBadUrl(b);
                    }
                    else
                    {
                        b.AppendCodePoint(await ReadEscapedChar());
                    }
                }
                else
                {
                    b.AppendCodePoint(c);
                }
            }
        }

        private bool IsNonPrinting(int c)
        {
            return (c >= 0 && c <= 8)
                || c == 11
                || (c >= 14 && c <= 31)
                || c == 127;
        }

        private async Task<Token> ReadBadUrl(StringBuilder b)
        {
            while (true)
            {
                int c = await Advance();
                if (c == EOF || c == ')')
                {
                    return Token(TokenType.BadUrl, b.ToString());
                }
                else if (c == '\\')
                {
                    var n = await Peek();
                    if (n == '\n')
                    {
                        b.AppendCodePoint(c);
                    }
                    else if (n != EOF)
                    {
                        b.AppendCodePoint(await ReadEscapedChar());
                    }
                }
                else
                {
                    b.AppendCodePoint(c);
                }
            }
        }

        private async Task<Token> ReadNumber()
        {
            var numStr = new StringBuilder();
            int c = await Advance();
            if (c == '+' || c == '-')
            {
                numStr.AppendCodePoint(c);
            }
            else
            {
                PushBack(c);
            }

            await AppendDigits(numStr);

            var next3 = await Peek3();
            bool integer = true;
            if (next3[0] == '.' && IsDigit(next3[1]))
            {
                integer = false;
                numStr.AppendCodePoint(await Advance());
                await AppendDigits(numStr);
                next3 = null;
            }

            next3 = next3 ?? await Peek3();

            if (next3[0] == 'e' || next3[0] == 'E')
            {
                if (IsDigit(next3[1]))
                {
                    numStr.AppendCodePoint(await Advance());
                    await AppendDigits(numStr);
                    next3 = null;
                }
                else if (next3[1] == '+' || next3[1] == '-')
                {
                    if (IsDigit(next3[2]))
                    {
                        numStr.AppendCodePoint(await Advance())
                            .AppendCodePoint(await Advance());
                        await AppendDigits(numStr);
                        next3 = null;
                    }
                }
            }

            string numForParse = numStr.ToString();

            if (numForParse.Length >= 2)
            {
                if (numForParse[0] == '.')
                {
                    numStr.Insert(0, '0');
                    numForParse = numForParse.Insert(0, "0");
                }
                else if ((numForParse[0] == '+' || numForParse[0] == '-') && numForParse[1] == '.')
                {
                    numForParse = numForParse.Insert(1, "0");
                }
            }
            var num = new Number
            {
                Type = integer ? NumberType.Integer : NumberType.Number,
                Value = decimal.Parse(numForParse, NumberStyles.Float)
            };

            next3 = next3 ?? await Peek3();

            if (next3[0] == '%')
            {
                numStr.AppendCodePoint(await Advance());
                var tok = Token(TokenType.Percentage, numStr.ToString());
                tok.Number = num;
                return tok;
            }
            else if (IsIdent(next3))
            {
                num.Unit = await ReadName();
                numStr.Append(num.Unit);

                var tok = Token(TokenType.Dimension, numStr.ToString());
                tok.Number = num;
                return tok;
            }
            else
            {
                var tok = Token(TokenType.Number, numStr.ToString());
                tok.Number = num;
                return tok;
            }
        }

        private bool IsIdent(int[] vals)
        {
            if (vals[0] == '-')
            {
                return vals[1] == '-'
                    || IsNameStartCodePoint(vals[1])
                    || (vals[1] == '\\' && vals[2] != EOF && vals[2] != '\n');
            }
            else if (IsNameStartCodePoint(vals[0]))
            {
                return true;
            }
            else if (vals[0] == '\'')
            {
                return vals[1] != EOF && vals[1] != '\n';
            }
            return false;

        }

        private async Task<string> ReadName()
        {
            var b = new StringBuilder();
            while (true)
            {
                int c = await Advance();
                if (IsNameCodePoint(c))
                {
                    b.AppendCodePoint(c);
                }
                else if (c == '\\')
                {
                    var n = await Peek();
                    if (n == EOF || n == '\n')
                    {
                        PushBack(c);
                        return b.ToString();
                    }
                    else
                    {
                        b.AppendCodePoint(await ReadEscapedChar());
                    }
                }
                else
                {
                    PushBack(c);
                    return b.ToString();
                }
            }
        }

        private async Task<int> ReadEscapedChar()
        {
            int c = await Advance();
            if (c == EOF || c == '\n')
            {
                throw new InvalidOperationException("Read a 0-length escape sequence");
            }

            if (IsHexDigit(c))
            {
                var hb = new StringBuilder();
                hb.AppendCodePoint(c);
                while (hb.Length < 6)
                {
                    c = await Advance();
                    if (c == EOF || IsWhitespace(c))
                    {
                        // End of sequence (whitespace consumed)
                        break;
                    }
                    else if (IsHexDigit(c))
                    {
                        hb.AppendCodePoint(c);
                    }
                    else
                    {
                        // End of sequence
                        PushBack(c);
                        break;
                    }
                }

                int val = Convert.ToInt32(hb.ToString(), 16);
                if (val == 0 || (val >= SurrogateLow && val <= SurrogateHigh) || val > UnicodeMax)
                {
                    return '�';
                }
                else
                {
                    return val;
                }
            }
            else
            {
                return c;
            }
        }

        private async Task AppendDigits(StringBuilder b)
        {
            while (true)
            {
                int c = await Advance();
                if (IsDigit(c))
                {
                    b.AppendCodePoint(c);
                }
                else
                {
                    PushBack(c);
                    break;
                }
            }
        }

        private bool IsCdo(int[] vals)
        {
            if (vals.Length != 3)
            {
                return false;
            }
            return vals[0] == '!' && vals[1] == '-' && vals[2] == '-';
        }

        private bool IsCdc(int[] vals)
        {
            if (vals.Length != 3)
            {
                return false;
            }
            return vals[0] == '-' && vals[1] == '-' && vals[2] == '>';
        }

        private bool IsNumber(int[] vals)
        {
            if (IsDigit(vals[0]))
            {
                return true;
            }
            if (vals[0] != '+' && vals[0] != '-' && vals[0] != '.')
            {
                return false;
            }

            if (IsDigit(vals[1]))
            {
                return true;
            }
            if (vals[1] != '.' || vals[0] == '.')
            {
                return false;
            }

            return IsDigit(vals[2]);
        }

        private async Task<int> Peek()
        {
            int c1 = await Advance();
            PushBack(c1);
            return c1;
        }

        private async Task<int[]> Peek3()
        {
            int c1 = await Advance();
            int c2 = await Advance();
            int c3 = await Advance();
            PushBack(c3);
            PushBack(c2);
            PushBack(c1);
            return new[] { c1, c2, c3 };
        }

        private async Task<Token> ReadWhitespace()
        {
            var b = new StringBuilder();
            while (true)
            {
                int current = await Advance();
                if (current == EOF)
                {
                    return Token(TokenType.Whitespace, b.ToString());
                }
                else if (!IsWhitespace(current))
                {
                    PushBack(current);
                    return Token(TokenType.Whitespace, b.ToString());
                }
                else
                {
                    b.AppendCodePoint(current);
                }
            }
        }

        private bool IsWhitespace(int c)
        {
            return c == '\n' || c == '\t' || c == ' ';
        }

        private async Task<Token> ReadComment()
        {
            var b = new StringBuilder();
            bool prevStar = false;
            while (true)
            {
                int c = await Advance();
                if (c == EOF)
                {
                    Console.Error.WriteLine("EOF in comment");
                    return Token(TokenType.Comment, b.ToString());
                }
                b.AppendCodePoint(c);
                if (c == '*')
                {
                    prevStar = true;
                }
                else if (c == '/' && prevStar)
                {
                    b.Length -= 2;
                    return Token(TokenType.Comment, b.ToString());
                }
                else
                {
                    prevStar = false;
                }
            }
        }

        private async Task<Token> ReadString(int end)
        {
            var b = new StringBuilder();
            while (true)
            {
                int c = await Advance();
                if (c == EOF)
                {
                    Console.Error.WriteLine("EOF on string");
                    return Token(TokenType.String, b.ToString());
                }
                else if (c == end)
                {
                    return Token(TokenType.String, b.ToString());
                }
                else if (c == '\n')
                {
                    PushBack(c);
                    return Token(TokenType.BadString, b.ToString());
                }
                else if (c == '\\')
                {
                    int c2 = await Peek();
                    if (c2 == EOF)
                    {
                        continue;
                    }
                    else if (c2 == '\n')
                    {
                        await Advance();
                        continue;
                    }

                    b.AppendCodePoint(await ReadEscapedChar());
                }
                else
                {
                    b.AppendCodePoint(c);
                }
            }
        }

        private bool IsHexDigit(int c)
        {
            return (c >= '0' && c <= '9')
                || (c >= 'a' && c <= 'f')
                || (c >= 'A' && c <= 'F');
        }

        private bool IsNameCodePoint(int c)
        {
            return IsNameStartCodePoint(c) || c == '-' || IsDigit(c);
        }

        private bool IsNameStartCodePoint(int c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_' || c > 128;
        }

        private bool IsDigit(int c)
        {
            return c >= '0' && c <= '9';
        }

        private Token Token(TokenType type, string value)
        {
            Debug.Assert(value.Length != _representation.Length || value == _representation.ToString());
            return new Token
            {
                Type = type,
                Value = value,
                Representation = value.Length == _representation.Length ? value : _representation.ToString(),
                Line = _tokenStartLine, 
                Position = _tokenStart,
            };
        }

        private Token Eof()
        {
            return Token(TokenType.EOF, "");
        }

        private async Task<int> Advance()
        {
            var c = _back.Count == 0 ? await _css.ReadAsync() : _back.Pop();
            _representation.AppendCodePoint(c);
            return c;
        }

        private void PushBack(int c)
        {
            if (c == EOF)
            {
                return;
            }
            _back.Push(c);
            _representation.Length -= (c <= ushort.MaxValue) ? 1 : 2;
        }

        public void Dispose()
        {
            _css.Dispose();
        }
    }
}
