namespace SupportToolkit.Core.Logging;

/// <summary>
/// Lightweight diagnostic logging for SupportToolkit operations.
///
/// INFO and DEBUG output are intended for development and troubleshooting
/// and are emitted only when SUPPORTTOOLKIT_DEBUG=true.
///
/// Warnings represent operator-relevant conditions and are always emitted.
///
/// Human-facing progress messages belong to the UI rather than this logger.
/// </summary>
public sealed class OperationalLogger
{
    public bool DebugEnabled { get; }

    public OperationalLogger(
        bool debugEnabled = false)
    {
        DebugEnabled = debugEnabled;
    }

    public static OperationalLogger FromEnvironment(
        Func<string, string?>? environmentReader = null)
    {
        environmentReader ??=
            Environment.GetEnvironmentVariable;

        return new OperationalLogger(
            string.Equals(
                environmentReader(
                    "SUPPORTTOOLKIT_DEBUG"
                ),
                "true",
                StringComparison.OrdinalIgnoreCase
            )
        );
    }

    public void Info(
        string message)
    {
        if (!DebugEnabled)
        {
            return;
        }

        Write(
            "INFO",
            message
        );
    }

    public void Debug(
        string message)
    {
        if (!DebugEnabled)
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