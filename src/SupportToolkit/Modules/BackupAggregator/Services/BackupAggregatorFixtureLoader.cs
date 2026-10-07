using System.Text.Json;
using SupportToolkit.Providers.Acronis.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.Services;

public sealed class BackupAggregatorFixtureLoader
{
    private const string FixtureFileName =
        "backup-activities-mixed.json";

    public async Task<IReadOnlyList<AcronisActivityDto>>
        LoadActivitiesAsync(
            CancellationToken cancellationToken = default)
    {
        var fixturePath =
            FindFixturePath();

        await using var stream =
            File.OpenRead(
                fixturePath
            );

        var page =
            await JsonSerializer.DeserializeAsync<AcronisActivityPageDto>(
                stream,
                cancellationToken:
                    cancellationToken
            )
            ?? throw new InvalidOperationException(
                $"Could not deserialize fixture '{fixturePath}'."
            );

        return page.Items;
    }

    private static string FindFixturePath()
    {
        var relativePath =
            Path.Combine(
                "Fixtures",
                "Acronis",
                FixtureFileName
            );

        /*
         * Running from src/SupportToolkit.
         */
        var directCandidate =
            Path.Combine(
                Directory.GetCurrentDirectory(),
                relativePath
            );

        if (File.Exists(
                directCandidate))
        {
            return directCandidate;
        }

        /*
         * Running from repository root.
         */
        var repositoryCandidate =
            Path.Combine(
                Directory.GetCurrentDirectory(),
                "src",
                "SupportToolkit",
                relativePath
            );

        if (File.Exists(
                repositoryCandidate))
        {
            return repositoryCandidate;
        }

        /*
         * Last fallback:
         * walk upward from the application directory and look for the
         * source-tree fixture.
         */
        var directory =
            new DirectoryInfo(
                AppContext.BaseDirectory
            );

        while (directory is not null)
        {
            var candidate =
                Path.Combine(
                    directory.FullName,
                    "src",
                    "SupportToolkit",
                    relativePath
                );

            if (File.Exists(
                    candidate))
            {
                return candidate;
            }

            candidate =
                Path.Combine(
                    directory.FullName,
                    relativePath
                );

            if (File.Exists(
                    candidate))
            {
                return candidate;
            }

            directory =
                directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate BackupAggregator fixture " +
            $"'{FixtureFileName}'."
        );
    }
}