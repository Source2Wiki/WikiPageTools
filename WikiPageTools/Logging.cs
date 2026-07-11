using System.Text;

namespace EntityPageTools
{
    static class Logging
    {
        public static bool Verbose = true;

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
