using System.Text.RegularExpressions;

namespace SupportToolkit.Tests.Architecture;

/// <summary>
/// Keeps the layers honest. Workflows and providers return models and
/// take callbacks; they do not write to a console, and they do not know
/// about the interfaces built on top of them (the console UI, the CLI, or
/// any future web UI). That is what lets another interface reuse them
/// without touching them.
///
/// These are text checks over the source files, so they hold regardless of
/// how projects are laid out. If one fails, move the console or interface
/// code up into Ui, Cli or a Console* reporter rather than extending an
/// allowlist. The allowlist below is only for host-level code that is
/// console-specific by design, and each entry is checked to still need it.
/// </summary>
public class LayeringTests
{
    private static readonly string[] LowerLayers =
        ["Core", "Providers", "Modules"];

    /// <summary>
    /// Files below the UI that may use the console, and why. Console
    /// reporters (Reporting/Console*.cs) and the CLI command classes of a
    /// module (Modules/*/*Module.cs) are adapters by role and need no
    /// entry.
    /// </summary>
    private static readonly Dictionary<string, string> ConsoleAllowlist =
        new()
        {
            ["Core/Configuration/FirstRunStartup.cs"] =
                "first-run prompts of the console host",

            ["Core/Secrets/ConsoleSecretReader.cs"] =
                "masked secret entry in the console wizard",

            ["Core/Logging/OperationalLogger.cs"] =
                "its default sink resolves Console.Out when no writer " +
                "is supplied",

            ["Providers/BusinessCentral/Auth/MsalBusinessCentralTokenProvider.cs"] =
                "device-code prompt defaults to Console.Out and is " +
                "replaceable through its constructor"
        };

    private static readonly Regex ConsoleUse =
        new(@"\bConsole\.", RegexOptions.CultureInvariant);

    private static readonly Regex UiOrCliReference =
        new(@"\bSupportToolkit\.(Ui|Cli)\b", RegexOptions.CultureInvariant);

    private static readonly Regex ModulesReference =
        new(@"\bSupportToolkit\.Modules\b", RegexOptions.CultureInvariant);

    private static readonly Regex UiFramework =
        new(
            @"\b(Microsoft\.AspNetCore|System\.Windows)\b",
            RegexOptions.CultureInvariant);

    [Fact]
    public void NothingBelowTheUi_WritesToTheConsole()
    {
        var offenders =
            SourceFiles(LowerLayers)
                .Where(file => !IsConsoleAdapter(file.RelativePath))
                .Where(file => ConsoleUse.IsMatch(file.Code))
                .Select(file => file.RelativePath)
                .ToArray();

        Assert.True(
            offenders.Length == 0,
            "These files use the console but are not console adapters. " +
            "Return data or accept a callback or TextWriter, and let the " +
            "console layer print it:\n  " +
            string.Join("\n  ", offenders));
    }

    [Fact]
    public void NothingBelowTheUi_ReferencesTheUiOrCliLayers()
    {
        var offenders =
            SourceFiles(LowerLayers)
                .Where(file => UiOrCliReference.IsMatch(file.Code))
                .Select(file => file.RelativePath)
                .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Workflows and providers must not depend on an interface:\n  " +
            string.Join("\n  ", offenders));
    }

    [Fact]
    public void CoreAndProviders_DoNotReferenceModules()
    {
        var offenders =
            SourceFiles(["Core", "Providers"])
                .Where(file => ModulesReference.IsMatch(file.Code))
                .Select(file => file.RelativePath)
                .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Core and Providers sit below Modules:\n  " +
            string.Join("\n  ", offenders));
    }

    private static readonly Regex ProvidersReference =
        new(@"\bSupportToolkit\.Providers\b", RegexOptions.CultureInvariant);

    /// <summary>
    /// Modules whose rules must not know which systems the data came from,
    /// with the folders that may still reference a provider: the Production
    /// adapter to the real systems, and for the Backup Aggregator its
    /// Fixtures, which fake Acronis so fixture mode runs the real adapter.
    /// </summary>
    private static readonly (string Module, string[] AllowedFolders)[] ProviderAgnosticModules =
    [
        ("Modules/UsageReport/", ["/Production/"]),
        ("Modules/BackupAggregator/", ["/Production/", "/Fixtures/"])
    ];

    [Theory]
    [InlineData("Modules/UsageReport/")]
    [InlineData("Modules/BackupAggregator/")]
    public void ProviderAgnosticModules_ReferenceProviders_OnlyInTheirProductionAdapter(
        string module)
    {
        var allowedFolders =
            ProviderAgnosticModules.Single(entry => entry.Module == module).AllowedFolders;

        var files =
            SourceFiles(["Modules"])
                .Where(file => file.RelativePath.StartsWith(module, StringComparison.Ordinal))
                .ToArray();

        Assert.NotEmpty(files);

        var offenders =
            files
                .Where(file => !allowedFolders.Any(
                    folder => file.RelativePath.Contains(folder, StringComparison.Ordinal)))
                .Where(file => ProvidersReference.IsMatch(file.Code))
                .Select(file => file.RelativePath)
                .ToArray();

        Assert.True(
            offenders.Length == 0,
            "These files depend on a provider. Map provider data to the " +
            "module's own types in its Production adapter instead:\n  " +
            string.Join("\n  ", offenders));
    }

    [Fact]
    public void NoWebOrDesktopUiFramework_IsReferencedBelowTheUi()
    {
        var offenders =
            SourceFiles(LowerLayers)
                .Where(file => UiFramework.IsMatch(file.Code))
                .Select(file => file.RelativePath)
                .ToArray();

        Assert.True(
            offenders.Length == 0,
            "A UI framework belongs in the UI layer only:\n  " +
            string.Join("\n  ", offenders));
    }

    [Fact]
    public void AspNetCore_IsOnlyUsedByTheWebUi_AndTheEntryPoint()
    {
        var offenders =
            AllSourceFiles()
                .Where(
                    file => !file.RelativePath.StartsWith(
                                "Ui/Web/",
                                StringComparison.Ordinal)
                            && file.RelativePath != "Program.cs")
                .Where(
                    file => file.Code.Contains(
                        "Microsoft.AspNetCore",
                        StringComparison.Ordinal))
                .Select(file => file.RelativePath)
                .ToArray();

        Assert.True(
            offenders.Length == 0,
            "The web framework belongs in Ui/Web (and Program.cs, which " +
            "starts it). Everything else stays usable without it:\n  " +
            string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Each transport's safety wiring (read-only handlers, no redirects,
    /// timeouts) is assembled in one pipeline file, so no other code can
    /// build a client that skips it.
    /// </summary>
    [Theory]
    [InlineData("new HttpClient(", "HttpPipeline.cs")]
    [InlineData("new HttpClientHandler", "HttpPipeline.cs")]
    [InlineData("new AcronisReadOnlyHandler(", "Providers/Acronis/Transport/AcronisHttpPipeline.cs")]
    public void HttpClients_AreBuiltOnlyInTheTransportPipelines(
        string construction,
        string allowedPathSuffix)
    {
        var files = AllSourceFiles().ToArray();

        // Not vacuous: the construction does occur, in a pipeline.
        Assert.Contains(
            files,
            file => file.RelativePath.EndsWith(allowedPathSuffix, StringComparison.Ordinal)
                    && file.Code.Contains(construction, StringComparison.Ordinal));

        var offenders =
            files
                .Where(file => !file.RelativePath.EndsWith(
                    allowedPathSuffix,
                    StringComparison.Ordinal))
                .Where(file => file.Code.Contains(
                    construction,
                    StringComparison.Ordinal))
                .Select(file => file.RelativePath)
                .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"'{construction}' belongs in a transport pipeline " +
            "(AcronisHttpPipeline, BusinessCentralHttpPipeline, " +
            "ZendeskTicketingHttpPipeline, ZendeskDeletionHttpPipeline):\n  " +
            string.Join("\n  ", offenders));
    }

    [Fact]
    public void RazorComponents_LiveOnlyInTheWebUi()
    {
        var root = SourceRoot();

        var offenders =
            Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories)
                .Select(
                    path => Path.GetRelativePath(root, path)
                        .Replace('\\', '/'))
                .Where(
                    path => !path.StartsWith(
                        "Ui/Web/",
                        StringComparison.Ordinal))
                .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Components belong in Ui/Web:\n  " +
            string.Join("\n  ", offenders));

        // And the check is not vacuous: the web UI does have components.
        Assert.NotEmpty(
            Directory.EnumerateFiles(
                Path.Combine(root, "Ui", "Web"),
                "*.razor",
                SearchOption.AllDirectories));
    }

    [Fact]
    public void TheAllowlist_HasNoStaleEntries()
    {
        var files =
            SourceFiles(LowerLayers)
                .ToDictionary(file => file.RelativePath);

        foreach (var (path, reason) in ConsoleAllowlist)
        {
            Assert.True(
                files.TryGetValue(path, out var file),
                $"Allowlisted file no longer exists: {path}");

            Assert.True(
                ConsoleUse.IsMatch(file!.Code),
                $"{path} no longer uses the console, so it does not " +
                $"need its allowlist entry ({reason}). Remove it.");
        }
    }

    [Fact]
    public void TheScan_ActuallyFinds_ConsoleUse_AndIgnoresComments()
    {
        Assert.Matches(ConsoleUse, "Console.WriteLine(\"x\");");
        Assert.Matches(ConsoleUse, "System.Console.Out.Write(x);");
        Assert.DoesNotMatch(ConsoleUse, "ConsoleProgress.Write(x);");

        Assert.DoesNotMatch(
            ConsoleUse,
            StripComments("// Console.WriteLine is not called here\n"));

        Assert.DoesNotMatch(
            ConsoleUse,
            StripComments("/* Console.Out */ var x = 1;"));

        // A URL in a string is not a comment.
        Assert.Matches(
            ConsoleUse,
            StripComments(
                "var u = \"https://example.test\"; Console.WriteLine(u);"));
    }

    [Fact]
    public void TheScan_SeesTheSourceTree()
    {
        var files = SourceFiles(LowerLayers).ToArray();

        Assert.True(files.Length > 100, "The source tree was not found.");

        Assert.Contains(
            files,
            file => file.RelativePath ==
                "Modules/BackupAggregator/Services/BackupReviewSession.cs");
    }

    private static bool IsConsoleAdapter(
        string relativePath)
    {
        if (ConsoleAllowlist.ContainsKey(relativePath))
        {
            return true;
        }

        var name = Path.GetFileName(relativePath);

        // Console reporters: .../Reporting/Console*.cs
        if (relativePath.Contains("/Reporting/", StringComparison.Ordinal)
            && name.StartsWith("Console", StringComparison.Ordinal))
        {
            return true;
        }

        // A module's CLI command class: Modules/<Name>/<Name>Module.cs
        return relativePath.StartsWith("Modules/", StringComparison.Ordinal)
               && name.EndsWith("Module.cs", StringComparison.Ordinal)
               && relativePath.Count(character => character == '/') == 2;
    }

    private sealed record SourceFile(
        string RelativePath,
        string Code);

    private static IEnumerable<SourceFile> SourceFiles(
        IEnumerable<string> layers)
    {
        var root = SourceRoot();

        foreach (var layer in layers)
        {
            var directory = Path.Combine(root, layer);

            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(
                         directory,
                         "*.cs",
                         SearchOption.AllDirectories))
            {
                var relative =
                    Path.GetRelativePath(root, path)
                        .Replace('\\', '/');

                yield return new SourceFile(
                    relative,
                    StripComments(File.ReadAllText(path)));
            }
        }
    }

    /// <summary>
    /// Every source file of the application, whatever the folder, without
    /// build output.
    /// </summary>
    private static IEnumerable<SourceFile> AllSourceFiles()
    {
        var root = SourceRoot();

        foreach (var path in Directory.EnumerateFiles(
                     root,
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            var relative =
                Path.GetRelativePath(root, path).Replace('\\', '/');

            if (relative.StartsWith("obj/", StringComparison.Ordinal)
                || relative.StartsWith("bin/", StringComparison.Ordinal))
            {
                continue;
            }

            yield return new SourceFile(
                relative,
                StripComments(File.ReadAllText(path)));
        }
    }

    /// <summary>
    /// Removes comments so a mention of the console in documentation does
    /// not count. A // inside a string such as a URL is left alone.
    /// </summary>
    private static string StripComments(
        string code)
    {
        var withoutBlocks =
            Regex.Replace(code, @"/\*.*?\*/", " ", RegexOptions.Singleline);

        return Regex.Replace(
            withoutBlocks,
            @"(^|\s)//[^\r\n]*",
            "$1",
            RegexOptions.Multiline);
    }

    private static string SourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate =
                Path.Combine(directory.FullName, "src", "SupportToolkit");

            if (File.Exists(
                    Path.Combine(directory.FullName, "SupportToolkit.slnx"))
                && Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "The repository root (SupportToolkit.slnx) was not found " +
            "above the test output.");
    }
}
