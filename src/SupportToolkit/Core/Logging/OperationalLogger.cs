namespace SupportToolkit.Core.Logging;

/// <summary>
/// Lightweight console logging for SupportToolkit operations.
/// INFO is reserved for operator-relevant milestones. Detailed transport and
/// pagination diagnostics should be emitted only when debug logging is enabled.
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
                environmentReader("SUPPORTTOOLKIT_DEBUG"),
                "true",
                StringComparison.OrdinalIgnoreCase
            )
        );
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
