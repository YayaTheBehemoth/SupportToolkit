
using System.Text.RegularExpressions;

namespace SupportToolkit.Core.Logging;

/// <summary>
/// Lightweight diagnostic logging for SupportToolkit operations.
///
/// INFO and DEBUG output are emitted only when
/// SUPPORTTOOLKIT_DEBUG=true.
///
/// Warnings are always emitted.
///
/// Identifier redaction reduces accidental disclosure in copied
/// diagnostic output. It does not guarantee that arbitrary messages
/// are free of other sensitive information.
///
/// Human-facing progress belongs to the UI.
/// </summary>
public sealed class OperationalLogger
{
    private static readonly Regex UuidPattern =
        new(
            @"(?<![0-9a-fA-F])" +
            @"[0-9a-fA-F]{8}-" +
            @"[0-9a-fA-F]{4}-" +
            @"[0-9a-fA-F]{4}-" +
            @"[0-9a-fA-F]{4}-" +
            @"[0-9a-fA-F]{12}" +
            @"(?![0-9a-fA-F])",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant
        );

    private readonly TextWriter? _output;

    public bool DebugEnabled { get; }

    public OperationalLogger(
        bool debugEnabled = false,
        TextWriter? output = null)
    {
        DebugEnabled = debugEnabled;
        _output = output;
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

    public void Info(string message)
    {
        if (!DebugEnabled)
        {
            return;
        }

        Write("INFO", message);
    }

    public void Debug(string message)
    {
        if (!DebugEnabled)
        {
            return;
        }

        Write("DEBUG", message);
    }

    public void Warning(string message)
    {
        Write("WARN", message);
    }

    private void Write(
        string level,
        string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var safeMessage =
            SanitizeMessage(message);

        /*
         * Resolve Console.Out at write time, preserving the existing
         * console behavior unless an explicit writer was supplied.
         *
         * An injected writer is useful for deterministic tests.
         */
        var output =
            _output ?? Console.Out;

        output.WriteLine(
            $"[{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z] " +
            $"[{level}] {safeMessage}"
        );
    }

    private static string SanitizeMessage(
        string message)
    {
        /*
         * Prevent user-controlled or vendor-supplied line breaks from
         * creating additional, misleading log entries.
         */
        var singleLine =
            message
                .Replace('\r', ' ')
                .Replace('\n', ' ');

        /*
         * Redact UUIDs regardless of which provider generated the
         * diagnostic message.
         *
         * Do not redact HTTP status codes, route names, elapsed time,
         * or numeric counters that remain useful for troubleshooting.
         */
        return UuidPattern.Replace(
            singleLine,
            "{uuid}"
        );
    }
}
