namespace Shirobot.Plugin.RssSubscriber;

internal static class BotLog
{
    public static void Info(string message) => Console.WriteLine(message);
    public static void Success(string message) => Console.WriteLine(message);
    public static void Warning(string message) => Console.WriteLine($"[警告] {message}");
    public static void Error(string message) => Console.Error.WriteLine(message);
}
