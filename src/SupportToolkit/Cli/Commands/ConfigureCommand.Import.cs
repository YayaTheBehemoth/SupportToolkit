using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Cli.Commands;

public sealed partial class ConfigureCommand
{
    /// <summary>
    /// configure import [file]: moves a settings file that was handed over
    /// into the folder the application reads its settings from. Without a
    /// file it looks in the folder the application is in.
    ///
    /// Settings carry no passwords or client secrets, so those still have
    /// to be entered on each machine.
    /// </summary>
    private async Task<int> ImportAsync(
        string? file)
    {
        var importer = new ConfigurationImporter(_configurationStore);

        var result =
            file is null
                ? await importer.ImportFromDirectoryAsync(
                    AppContext.BaseDirectory)
                : await importer.ImportFileAsync(file);

        switch (result.Outcome)
        {
            case ConfigurationImportOutcome.Imported:
                Console.WriteLine("Settings imported.");
                Console.WriteLine(
                    $"  Moved:  {result.Source}");
                Console.WriteLine(
                    $"  To:     {result.Target}");

                if (result.Backup is not null)
                {
                    Console.WriteLine(
                        "  The settings that were there before are kept as:");
                    Console.WriteLine($"          {result.Backup}");
                }

                WriteSkipped(result);

                return await PromptForMissingSecretsAsync();

            case ConfigurationImportOutcome.AlreadyInPlace:
                Console.WriteLine(
                    "That file is already the settings file in use. " +
                    "Nothing was changed.");

                return 0;

            case ConfigurationImportOutcome.MoreThanOneFound:
                Console.WriteLine(
                    "More than one settings file was found, so none was " +
                    "used:");

                foreach (var candidate in result.Candidates)
                {
                    Console.WriteLine($"  {candidate}");
                }

                Console.WriteLine(
                    "Leave only the one you want in this folder and run " +
                    "it again.");

                return 1;

            case ConfigurationImportOutcome.NotAConfiguration:
                Console.WriteLine(
                    "That is not a SupportToolkit settings file. Nothing " +
                    "was changed.");

                WriteSkipped(result);

                return 1;

            default:
                Console.WriteLine(
                    "No settings file was found in " +
                    AppContext.BaseDirectory);
                Console.WriteLine(
                    "Put the file you were sent in that folder (next to " +
                    "SupportToolkit.exe) and run this again.");

                WriteSkipped(result);

                return 1;
        }
    }

    /// <summary>
    /// The settings file holds no secrets, so after an import the ones the
    /// connections need are asked for here, hidden as they are typed, and
    /// stored the same way the wizard stores them. Enter skips one.
    /// </summary>
    private async Task<int> PromptForMissingSecretsAsync()
    {
        Console.WriteLine();

        IReadOnlyList<MissingSecret> missing;

        try
        {
            missing =
                await RequiredSecrets.FindMissingAsync(
                    await _configurationStore.LoadAsync(),
                    _secretStore);
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine(
                "The settings could not be checked for missing secrets. " +
                "Run: SupportToolkit configure status");

            return 0;
        }

        if (missing.Count == 0)
        {
            Console.WriteLine(
                "This computer already has every secret these settings need.");

            return 0;
        }

        Console.WriteLine(
            "The settings file holds no passwords or client secrets. " +
            "These connections need one on this computer:");

        foreach (var item in missing)
        {
            Console.WriteLine(
                $"  {item.Provider} connection '{item.ConnectionName}'");
        }

        Console.WriteLine();
        Console.WriteLine(
            "Enter each one now. What you type is not shown. Press Enter " +
            "to skip one.");

        var skipped = new List<MissingSecret>();
        var stored = 0;

        for (var index = 0; index < missing.Count; index++)
        {
            var item = missing[index];

            string value;

            try
            {
                value =
                    ConsoleSecretReader.ReadOptional(
                        $"  {item.Provider} '{item.ConnectionName}' " +
                        "client secret: ");
            }
            catch (InvalidOperationException)
            {
                // No interactive console: nothing can be typed.
                skipped.AddRange(missing.Skip(index));

                Console.WriteLine(
                    "Secrets can only be entered in an interactive window.");

                break;
            }

            if (string.IsNullOrEmpty(value))
            {
                skipped.Add(item);
                continue;
            }

            await _secretStore.SetSecretAsync(item.Key, value);
            stored++;
        }

        Console.WriteLine();

        if (stored > 0)
        {
            Console.WriteLine($"Stored {stored} secret(s) on this computer.");
        }

        if (skipped.Count > 0)
        {
            Console.WriteLine("Still missing:");

            foreach (var item in skipped)
            {
                Console.WriteLine(
                    $"  {item.Provider} connection '{item.ConnectionName}'");
            }

            Console.WriteLine(
                "Enter them later with SupportToolkit configure; " +
                "SupportToolkit configure status shows what is missing.");
        }

        return 0;
    }

    private static void WriteSkipped(
        ConfigurationImportResult result)
    {
        if (result.Skipped.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Looked at and left alone:");

        foreach (var line in result.Skipped)
        {
            Console.WriteLine($"  {line}");
        }
    }
}
