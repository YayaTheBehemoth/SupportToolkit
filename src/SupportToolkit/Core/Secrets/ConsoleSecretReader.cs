using System.Text;

namespace SupportToolkit.Core.Secrets;

/// <summary>
/// Reads a secret interactively without displaying its contents.
/// </summary>
public static class ConsoleSecretReader
{
    public static string ReadOptional(
        string prompt)
    {
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException(
                "Secure secret entry requires an interactive console."
            );
        }

        Console.Write(
            prompt
        );

        var value =
            new StringBuilder();

        while (true)
        {
            var key =
                Console.ReadKey(
                    intercept:
                        true
                );

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();

                return value.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Length == 0)
                {
                    continue;
                }

                value.Length--;

                Console.Write(
                    "\b \b"
                );

                continue;
            }

            if (key.Key == ConsoleKey.C
                && key.Modifiers.HasFlag(
                    ConsoleModifiers.Control
                ))
            {
                Console.WriteLine();

                throw new OperationCanceledException(
                    "Secret entry was cancelled."
                );
            }

            if (char.IsControl(
                    key.KeyChar))
            {
                continue;
            }

            value.Append(
                key.KeyChar
            );

            Console.Write(
                '*'
            );
        }
    }
}