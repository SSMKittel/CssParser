namespace CssParser.Lexer
{
    public class Token
    {
        public TokenType Type;
        public string Value;
        public uint Position;
        public uint Line;

        public Number? Number;
        internal bool IsHashId;
    }

    public struct Number
    {
        public decimal Value;
        public NumberType Type;
        public string Unit;
    }
}
