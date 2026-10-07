using System.Text.Json;
using SupportToolkit.Providers.Acronis.Activities.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.Services;

public sealed class BackupAggregatorFixtureLoader
{
    public async Task<IReadOnlyList<AcronisActivityDto>>
        LoadActivitiesAsync(
            string fixtureFileName,
            CancellationToken cancellationToken = default)
    {
        var fixturePath =
            FindFixturePath(
                fixtureFileName
            );

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

    private static string FindFixturePath(
        string fixtureFileName)
    {
        var relativePath =
            Path.Combine(
                "Fixtures",
                "Acronis",
                fixtureFileName
            );

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
            $"'{fixtureFileName}'."
        );
    }
}