using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntityPageTools
{
    static class Logging
    {
        public static bool Verbose = true;

        /// <summary>
        /// The renderer wants an ILogger. It is chatty about shaders and none of it belongs in a
        /// dump log, so it goes nowhere unless something actually fails, which surfaces as an
        /// exception instead.
        /// </summary>
        public static ILogger RendererLogger { get; } = NullLogger.Instance;

        private const char BannerChar = '-';

        private static ConsoleColor OriginalForeColor;

        public static string BannerTitle(string title, int bannerLength = 100)
        {
            var bannerCharAmount = bannerLength - title.Length;

            var bannerSideStringBuilder = new StringBuilder();

            var sideLengthDivResults = Math.DivRem((byte)bannerCharAmount, (byte)2.0f);
            for (int i = 0; i < sideLengthDivResults.Quotient; i++)
            {
                bannerSideStringBuilder.Append(BannerChar);
            }

            var bannerSideString = bannerSideStringBuilder.ToString();
            var finalString = $"{bannerSideString}{title}{bannerSideString}";
            if (sideLengthDivResults.Remainder != 0)
            {
                finalString += BannerChar;
            }

            return finalString;
        }

        public static void Log(string message = "", ConsoleColor color = ConsoleColor.White)
        {
            OriginalForeColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ForegroundColor = OriginalForeColor;
        }

        public static void LogS(string message = "", ConsoleColor color = ConsoleColor.White)
        {
            OriginalForeColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(message);
            Console.ForegroundColor = OriginalForeColor;
        }
    }
}
