namespace SupportToolkit.Ui;

/// <summary>
/// The console's sink for review progress messages.
///
/// Workflows do not write to the console; the console host hands them this
/// sink. Reviews run tenants concurrently, so writes are serialized.
/// </summary>
public static class ConsoleProgress
{
    private static readonly object ConsoleLock = new();

    public static void Write(
        string message)
    {
        lock (ConsoleLock)
        {
            Console.WriteLine(message);
        }
    }
}
