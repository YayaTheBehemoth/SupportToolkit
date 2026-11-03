using SupportToolkit.Core.Configuration;

namespace SupportToolkit.Tests.Core.Configuration;

/// <summary>
/// A settings file handed over by hand is moved to where the application
/// reads it, exactly as it is, and never over existing settings without a
/// backup.
/// </summary>
public class ConfigurationImporterTests : IDisposable
{
    private const string SettingsJson = """
        {
          "activeProfile": "work",
          "futureSetting": { "kept": [1, 2, 3] },
          "profiles": {
            "work": { "modules": { "backup-aggregator": "fixture" } }
          }
        }
        """;

    private readonly string _root =
        Directory.CreateTempSubdirectory("st-import-").FullName;

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private string Folder(string name)
    {
        var path = Path.Combine(_root, name);

        Directory.CreateDirectory(path);

        return path;
    }

    private string Put(string folder, string name, string content)
    {
        var path = Path.Combine(folder, name);

        File.WriteAllText(path, content);

        return path;
    }

    private (ConfigurationImporter Importer, string Target) NewImporter()
    {
        // The settings folder does not exist yet, like a fresh machine.
        var target = Path.Combine(_root, "appdata", "local.config.json");

        return (
            new ConfigurationImporter(
                new JsonSupportToolkitConfigurationStore(target),
                new FixedClock()),
            target);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(2026, 11, 3, 9, 30, 15, TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public async Task ASettingsFile_IsMovedIntoPlace_ExactlyAsItWas()
    {
        var (importer, target) = NewImporter();
        var folder = Folder("unpacked");
        var source = Put(folder, "work.config.json", SettingsJson);

        var result = await importer.ImportFromDirectoryAsync(folder);

        Assert.Equal(ConfigurationImportOutcome.Imported, result.Outcome);
        Assert.Equal(target, result.Target);
        Assert.Null(result.Backup);

        // Moved, not copied: it is not left where it was unpacked.
        Assert.False(File.Exists(source));

        // And untouched, including a setting this build does not know.
        Assert.Equal(SettingsJson, File.ReadAllText(target));
    }

    [Fact]
    public async Task TheFileCanHaveAnyName_BecauseItIsRecognisedByItsContent()
    {
        var (importer, target) = NewImporter();
        var folder = Folder("unpacked");

        Put(folder, "settings (1).json", SettingsJson);

        var result = await importer.ImportFromDirectoryAsync(folder);

        Assert.Equal(ConfigurationImportOutcome.Imported, result.Outcome);
        Assert.True(File.Exists(target));
    }

    [Fact]
    public async Task ExistingSettings_AreKeptAsADatedBackup_NeverOverwritten()
    {
        var (importer, target) = NewImporter();
        var folder = Folder("unpacked");

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, """{ "profiles": { "old": {} } }""");

        Put(folder, "new.config.json", SettingsJson);

        var result = await importer.ImportFromDirectoryAsync(folder);

        Assert.Equal(ConfigurationImportOutcome.Imported, result.Outcome);
        Assert.Equal(target + ".bak-20261103-093015", result.Backup);
        Assert.Equal(
            """{ "profiles": { "old": {} } }""",
            File.ReadAllText(result.Backup!));
        Assert.Equal(SettingsJson, File.ReadAllText(target));
    }

    [Fact]
    public async Task ASecondBackupInTheSameSecond_DoesNotReplaceTheFirst()
    {
        var (importer, target) = NewImporter();
        var folder = Folder("unpacked");

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, """{ "profiles": { "first": {} } }""");

        Put(folder, "a.config.json", SettingsJson);
        var one = await importer.ImportFromDirectoryAsync(folder);

        Put(folder, "b.config.json", SettingsJson);
        var two = await importer.ImportFromDirectoryAsync(folder);

        Assert.NotEqual(one.Backup, two.Backup);
        Assert.True(File.Exists(one.Backup));
        Assert.True(File.Exists(two.Backup));
    }

    [Fact]
    public async Task TheFilesThatShipWithTheApp_AreNeverTreatedAsSettings()
    {
        var (importer, target) = NewImporter();
        var folder = Folder("app");

        Put(folder, "SupportToolkit.deps.json", """{ "profiles": { "x": {} } }""");
        Put(folder, "SupportToolkit.runtimeconfig.json", """{ "profiles": { "x": {} } }""");
        Put(folder, "SupportToolkit.staticwebassets.endpoints.json", """{ "profiles": { "x": {} } }""");

        var result = await importer.ImportFromDirectoryAsync(folder);

        Assert.Equal(ConfigurationImportOutcome.NothingFound, result.Outcome);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task AJsonFileThatIsNotSettings_IsLeftAlone_AndSaidSo()
    {
        var (importer, target) = NewImporter();
        var folder = Folder("unpacked");

        var other = Put(folder, "notes.json", """{ "hello": "world" }""");
        var broken = Put(folder, "broken.json", "{ not json");

        var result = await importer.ImportFromDirectoryAsync(folder);

        Assert.Equal(ConfigurationImportOutcome.NothingFound, result.Outcome);
        Assert.Equal(2, result.Skipped.Count);
        Assert.Contains(result.Skipped, line => line.StartsWith("notes.json"));
        Assert.Contains(result.Skipped, line => line.StartsWith("broken.json"));
        Assert.True(File.Exists(other));
        Assert.True(File.Exists(broken));
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task TwoSettingsFiles_AreAmbiguous_SoNeitherIsUsed()
    {
        var (importer, target) = NewImporter();
        var folder = Folder("unpacked");

        Put(folder, "one.config.json", SettingsJson);
        Put(folder, "two.config.json", SettingsJson);

        var result = await importer.ImportFromDirectoryAsync(folder);

        Assert.Equal(ConfigurationImportOutcome.MoreThanOneFound, result.Outcome);
        Assert.Equal(
            ["one.config.json", "two.config.json"],
            result.Candidates);
        Assert.True(File.Exists(Path.Combine(folder, "one.config.json")));
        Assert.True(File.Exists(Path.Combine(folder, "two.config.json")));
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task AnEmptyFolder_FindsNothing()
    {
        var (importer, _) = NewImporter();

        var result = await importer.ImportFromDirectoryAsync(Folder("empty"));

        Assert.Equal(ConfigurationImportOutcome.NothingFound, result.Outcome);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public async Task OnlyTheFolderItself_IsLookedAt_NotItsSubfolders()
    {
        var (importer, _) = NewImporter();
        var folder = Folder("unpacked");
        var nested = Directory.CreateDirectory(Path.Combine(folder, "nested")).FullName;

        Put(nested, "deep.config.json", SettingsJson);

        var result = await importer.ImportFromDirectoryAsync(folder);

        Assert.Equal(ConfigurationImportOutcome.NothingFound, result.Outcome);
    }

    [Fact]
    public async Task ANamedFile_IsImported_LikeAFoundOne()
    {
        var (importer, target) = NewImporter();
        var source = Put(Folder("downloads"), "from-colleague.json", SettingsJson);

        var result = await importer.ImportFileAsync(source);

        Assert.Equal(ConfigurationImportOutcome.Imported, result.Outcome);
        Assert.False(File.Exists(source));
        Assert.Equal(SettingsJson, File.ReadAllText(target));
    }

    [Fact]
    public async Task ANamedFileThatIsNotSettings_ChangesNothing()
    {
        var (importer, target) = NewImporter();
        var source = Put(Folder("downloads"), "other.json", """{ "a": 1 }""");

        var result = await importer.ImportFileAsync(source);

        Assert.Equal(ConfigurationImportOutcome.NotAConfiguration, result.Outcome);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task ANamedFileThatDoesNotExist_IsReported_NotThrown()
    {
        var (importer, _) = NewImporter();

        var result =
            await importer.ImportFileAsync(Path.Combine(_root, "missing.json"));

        Assert.Equal(ConfigurationImportOutcome.NotAConfiguration, result.Outcome);
    }

    [Fact]
    public async Task TheFileAlreadyInUse_IsNotMovedOntoItself()
    {
        var (importer, target) = NewImporter();

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, SettingsJson);

        var result = await importer.ImportFileAsync(target);

        Assert.Equal(ConfigurationImportOutcome.AlreadyInPlace, result.Outcome);
        Assert.Equal(SettingsJson, File.ReadAllText(target));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target)!, "*.bak*"));
    }

    [Fact]
    public async Task TheLiveSettingsFile_IsNotCountedAsACandidate_WhenItSitsInTheFolderScanned()
    {
        var (importer, target) = NewImporter();

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, SettingsJson);

        var result =
            await importer.ImportFromDirectoryAsync(
                Path.GetDirectoryName(target)!);

        Assert.Equal(ConfigurationImportOutcome.NothingFound, result.Outcome);
        Assert.Equal(SettingsJson, File.ReadAllText(target));
    }

    [Fact]
    public async Task ARealSavedConfiguration_CanBeImported_AndThenLoaded()
    {
        var (importer, target) = NewImporter();
        var folder = Folder("unpacked");

        var configuration = new SupportToolkitConfiguration { ActiveProfile = "ops" };
        configuration.Profiles["ops"] = new SupportToolkitProfile();

        var source = Path.Combine(folder, "export.config.json");
        await new JsonSupportToolkitConfigurationStore(source).SaveAsync(configuration);

        var result = await importer.ImportFromDirectoryAsync(folder);

        Assert.Equal(ConfigurationImportOutcome.Imported, result.Outcome);

        var loaded = await new JsonSupportToolkitConfigurationStore(target).LoadAsync();

        Assert.Equal("ops", loaded.ActiveProfile);
        Assert.Contains("ops", loaded.Profiles.Keys);
    }
}
