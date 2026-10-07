using System.Text.Json;
using SupportToolkit.Providers.Acronis.Alerts.Dtos;
namespace SupportToolkit.Tests;

public class AlertContractTests
{
    [Fact]
    public async Task AlertFixture_DeserializesSuccessfully()
    {
        var path = Path.Combine(
            "Fixtures",
            "Acronis",
            "alerts.json"
        );

        var json = await File.ReadAllTextAsync(path);

        var result = JsonSerializer.Deserialize<AlertPageDto>(json);

        Assert.NotNull(result);
        Assert.Equal(2, result.Items.Count);

        var backupFailed = result.Items[0];

        Assert.Equal(
            "BackupFailed",
            backupFailed.Type
        );

        Assert.Equal(
            "critical",
            backupFailed.Severity
        );

        Assert.Equal(
            "Backup",
            backupFailed.Category
        );

        Assert.Equal(
            "22222222-2222-2222-2222-222222222222",
            backupFailed.Details
                .GetProperty("resourceId")
                .GetString()
        );

        Assert.Equal(
            "FILES-01",
            backupFailed.Details
                .GetProperty("resourceName")
                .GetString()
        );
    }
}
