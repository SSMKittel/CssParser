using CssParser.Lexer;
using System;
using System.IO;
using System.Text;

namespace CssParser
{
    class Program
    {
        static void Main(string[] args)
        {
            var ms = new MemoryStream();
            ms.Write(Encoding.UTF8.GetBytes(@"p {
  color: red;
  text-align: center;
}
#myItems {
  list-style: square url(http://www.example.com/image.png);
  background: url(""banner.png"") #00F no-repeat fixed;
}"));
            ms.Seek(0, SeekOrigin.Begin);

            using (var r = new StreamReader(ms))
            using (CssLexer lex = new CssLexer(r))
            {
                while (true)
                {
                    var n = lex.Next().Result;
                    Console.WriteLine($"{n.Type}; [{n.Value}]");
                    if (n.Type == TokenType.EOF)
                    {
                        break;
                    }
                }
            }
            Console.Read();
        }
    }
}
