using SupportToolkit.Core.ErrorHandling;
using SupportToolkit.Core.Modules;
using SupportToolkit.Modules.BackupHealth;

ISupportToolkitModule[] modules =
[
    new BackupHealthModule()
];

try
{
    if (args.Length == 0 ||
        args[0] is "--help" or "-h")
    {
        PrintUsage(modules);
        return 0;
    }

    var command =
        args[0];

    var module =
        modules.FirstOrDefault(
            candidate =>
                string.Equals(
                    candidate.Command,
                    command,
                    StringComparison.OrdinalIgnoreCase
                )
        );

    if (module is null)
    {
        Console.Error.WriteLine(
            $"Unknown module: {command}"
        );

        Console.Error.WriteLine();

        PrintUsage(modules);

        return 1;
    }

    return await module.RunAsync(
        args[1..]
    );
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"ERROR: {ConsoleErrorFormatter.Format(exception)}"
    );

    if (IsDebugEnabled())
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(exception);
    }

    return 1;
}

static void PrintUsage(
    IEnumerable<ISupportToolkitModule> modules)
{
    Console.WriteLine(
        "SupportToolkit"
    );

    Console.WriteLine();

    Console.WriteLine(
        "Usage:"
    );

    Console.WriteLine(
        "  SupportToolkit <module> [options]"
    );

    Console.WriteLine();

    Console.WriteLine(
        "Modules:"
    );

    foreach (var module in modules)
    {
        Console.WriteLine(
            $"  {module.Command,-20} {module.Description}"
        );
    }
}

static bool IsDebugEnabled()
{
    return string.Equals(
        Environment.GetEnvironmentVariable(
            "SUPPORTTOOLKIT_DEBUG"
        ),
        "true",
        StringComparison.OrdinalIgnoreCase
    );
}