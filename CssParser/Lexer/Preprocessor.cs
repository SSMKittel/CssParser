using System;
using System.IO;
using System.Threading.Tasks;

namespace CssParser.Lexer
{
    internal class Preprocessor : IDisposable
    {
        public const int EOF = 0;
        public const int ERR = '�';

        private readonly TextReader _css;
        private readonly char[] _buf;
        private bool _reuseBuf;

        public Preprocessor(TextReader css)
        {
            _css = css;
            _buf = new char[1];
            _reuseBuf = false;
        }

        public async Task<int> ReadAsync()
        {
            if (_reuseBuf)
            {
                return _buf[0];
            }

            var read = await _css.ReadAsync(_buf, 0, 1);
            if (read == 0)
            {
                return EOF;
            }

            char c = _buf[0];
            if (c == '\0')
            {
                return ERR;
            }
            else if (char.IsHighSurrogate(c))
            {
                read = await _css.ReadAsync(_buf, 0, 1);
                if (read == 0)
                {
                    return ERR;
                }

                char c2 = _buf[0];
                if (char.IsLowSurrogate(c2))
                {
                    return char.ConvertToUtf32(c, c2);
                }
                else
                {
                    _reuseBuf = true;
                    return ERR;
                }
            }
            else if (char.IsLowSurrogate(c))
            {
                return ERR;
            }
            else if (c == '\r')
            {
                read = await _css.ReadAsync(_buf, 0, 1);
                if (read == 0)
                {
                    return '\n';
                }
                char c2 = _buf[0];
                if (c2 != '\n')
                {
                    _reuseBuf = true;
                }
                return '\n';
            }
            else if (c == '\f')
            {
                return '\n';
            }
            // We can't get Surrogate code points as we are already dealing with utf16
            else
            {
                return c;
            }
        }

        public void Dispose()
        {
            _css.Dispose();
        }
    }
}
