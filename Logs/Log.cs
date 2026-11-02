namespace enfold2minerva.Logs
{
    internal static class Log
    {
        public static void Info(string message) => Console.WriteLine(message);
        public static void Success(string message) => Write(message, ConsoleColor.Green);
        public static void Warn(string message) => Write(message, ConsoleColor.Yellow);
        public static void Error(string message) => Write(message, ConsoleColor.Red, Console.Error);

        private static void Write(string message, ConsoleColor color, TextWriter? writer = null)
        {
            Console.ForegroundColor = color;
            (writer ?? Console.Out).WriteLine(message);
            Console.ResetColor();
        }
    }
}