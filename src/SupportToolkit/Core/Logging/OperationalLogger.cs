namespace SupportToolkit.Core.Logging;

/// <summary>
/// Provides lightweight operational logging for SupportToolkit console runs.
///
/// Logging intentionally avoids payload contents, credentials, bearer tokens,
/// tenant identifiers, cursors, and customer-specific data.
/// </summary>
public sealed class OperationalLogger
{
    private readonly bool _debugEnabled;

    public OperationalLogger(
        bool debugEnabled = false)
    {
        _debugEnabled = debugEnabled;
    }

    public void Info(
        string message)
    {
        Write(
            "INFO",
            message
        );
    }

    public void Debug(
        string message)
    {
        if (!_debugEnabled)
        {
            return;
        }

        Write(
            "DEBUG",
            message
        );
    }

    public void Warning(
        string message)
    {
        Write(
            "WARN",
            message
        );
    }

    private static void Write(
        string level,
        string message)
    {
        Console.WriteLine(
            $"[{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z] " +
            $"[{level}] {message}"
        );
    }
}