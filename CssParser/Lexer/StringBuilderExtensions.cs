using System.Text;

namespace CssParser.Lexer
{
    internal static class StringBuilderExtensions
    {
        public static StringBuilder AppendCodePoint(this StringBuilder b, int c)
        {
            if (c <= char.MaxValue)
            {
                return b.Append((char)c);
            }
            return b.Append(char.ConvertFromUtf32(c));
        }
    }
}
