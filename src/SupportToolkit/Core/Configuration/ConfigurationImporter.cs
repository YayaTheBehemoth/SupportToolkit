using System.Globalization;

namespace SupportToolkit.Core.Configuration;

public enum ConfigurationImportOutcome
{
    /// <summary>The file was moved into place.</summary>
    Imported,

    /// <summary>The file given is already the active settings file.</summary>
    AlreadyInPlace,

    /// <summary>No settings file was found where it was looked for.</summary>
    NothingFound,

    /// <summary>More than one settings file was found, so none was chosen.</summary>
    MoreThanOneFound,

    /// <summary>The file given is not a SupportToolkit settings file.</summary>
    NotAConfiguration
}

/// <summary>
/// What an import did. Skipped lists files that were looked at and left
/// alone, with the reason.
/// </summary>
public sealed record ConfigurationImportResult(
    ConfigurationImportOutcome Outcome,
    string? Source,
    string Target,
    string? Backup,
    IReadOnlyList<string> Candidates,
    IReadOnlyList<string> Skipped);

/// <summary>
/// Moves a settings file that was handed over (for example by a password
/// manager) into the place the application reads its settings from.
///
/// It looks at what a file contains, not what it is called: a SupportToolkit
/// settings file is JSON with at least one profile. Files that belong to the
/// package itself are ignored. If settings already exist they are kept as a
/// dated backup next to them, never overwritten. The file is moved, not
/// copied, so it is not left lying around where it was unpacked, and it is
/// moved as it is: nothing in it is read out, changed or logged.
///
/// Settings files hold no passwords or client secrets; those are stored
/// separately and are not part of an import.
///
/// It contains no console code.
/// </summary>
public sealed class ConfigurationImporter
{
    // Files that ship with the application and are never settings.
    private static readonly string[] PackageFileSuffixes =
    [
        ".deps.json",
        ".runtimeconfig.json",
        ".endpoints.json",
        ".staticwebassets.json"
    ];

    private readonly ISupportToolkitConfigurationStore _target;
    private readonly TimeProvider _timeProvider;

    public ConfigurationImporter(
        ISupportToolkitConfigurationStore target,
        TimeProvider? timeProvider = null)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Finds the settings file in a folder (not its subfolders) and moves
    /// it into place. Exactly one is expected.
    /// </summary>
    public async Task<ConfigurationImportResult> ImportFromDirectoryAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var targetPath = Path.GetFullPath(_target.ConfigurationPath);
        var valid = new List<string>();
        var skipped = new List<string>();

        foreach (var file in Directory
                     .EnumerateFiles(directory, "*.json")
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);

            if (IsPackageFile(name)
                || string.Equals(
                    Path.GetFullPath(file),
                    targetPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var problem = await CheckAsync(file, cancellationToken);

            if (problem is null)
            {
                valid.Add(file);
            }
            else
            {
                skipped.Add($"{name}: {problem}");
            }
        }

        if (valid.Count == 0)
        {
            return Result(
                ConfigurationImportOutcome.NothingFound,
                null,
                null,
                [],
                skipped);
        }

        if (valid.Count > 1)
        {
            return Result(
                ConfigurationImportOutcome.MoreThanOneFound,
                null,
                null,
                valid.Select(Path.GetFileName).OfType<string>().ToArray(),
                skipped);
        }

        return await MoveIntoPlaceAsync(valid[0], skipped, cancellationToken);
    }

    /// <summary>
    /// Moves one named file into place.
    /// </summary>
    public async Task<ConfigurationImportResult> ImportFileAsync(
        string file,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);

        var full = Path.GetFullPath(file);

        if (string.Equals(
                full,
                Path.GetFullPath(_target.ConfigurationPath),
                StringComparison.OrdinalIgnoreCase))
        {
            return Result(
                ConfigurationImportOutcome.AlreadyInPlace,
                full,
                null,
                [],
                []);
        }

        var problem =
            File.Exists(full)
                ? await CheckAsync(full, cancellationToken)
                : "the file does not exist.";

        if (problem is not null)
        {
            return Result(
                ConfigurationImportOutcome.NotAConfiguration,
                full,
                null,
                [],
                [$"{Path.GetFileName(full)}: {problem}"]);
        }

        return await MoveIntoPlaceAsync(full, [], cancellationToken);
    }

    private async Task<ConfigurationImportResult> MoveIntoPlaceAsync(
        string source,
        IReadOnlyList<string> skipped,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var targetPath = Path.GetFullPath(_target.ConfigurationPath);

        var directory =
            Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException(
                "The settings folder could not be worked out.");

        Directory.CreateDirectory(directory);

        string? backup = null;

        if (File.Exists(targetPath))
        {
            backup = BackupPathFor(targetPath);
            File.Move(targetPath, backup);
        }

        try
        {
            File.Move(source, targetPath);
        }
        catch
        {
            // Put the old settings back; an import that fails changes
            // nothing.
            if (backup is not null && !File.Exists(targetPath))
            {
                File.Move(backup, targetPath);
            }

            throw;
        }

        await Task.CompletedTask;

        return Result(
            ConfigurationImportOutcome.Imported,
            source,
            backup,
            [],
            skipped);
    }

    private string BackupPathFor(string targetPath)
    {
        var stamp =
            _timeProvider
                .GetLocalNow()
                .ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        var path = $"{targetPath}.bak-{stamp}";

        for (var counter = 2; File.Exists(path); counter++)
        {
            path = $"{targetPath}.bak-{stamp}-{counter}";
        }

        return path;
    }

    /// <summary>
    /// Null when the file is a SupportToolkit settings file, otherwise
    /// the reason it is not.
    /// </summary>
    private static async Task<string?> CheckAsync(
        string file,
        CancellationToken cancellationToken)
    {
        try
        {
            var configuration =
                await new JsonSupportToolkitConfigurationStore(file)
                    .LoadAsync(cancellationToken);

            return configuration.Profiles.Count == 0
                ? "not a SupportToolkit settings file (it has no profiles)."
                : null;
        }
        catch (InvalidOperationException)
        {
            return "not a SupportToolkit settings file (it could not be read as one).";
        }
        catch (IOException exception)
        {
            return "it could not be read: " + exception.Message;
        }
        catch (UnauthorizedAccessException)
        {
            return "it could not be read: access was refused.";
        }
    }

    private ConfigurationImportResult Result(
        ConfigurationImportOutcome outcome,
        string? source,
        string? backup,
        IReadOnlyList<string> candidates,
        IReadOnlyList<string> skipped) =>
        new(
            outcome,
            source,
            Path.GetFullPath(_target.ConfigurationPath),
            backup,
            candidates,
            skipped);

    private static bool IsPackageFile(string name) =>
        PackageFileSuffixes.Any(
            suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
}
