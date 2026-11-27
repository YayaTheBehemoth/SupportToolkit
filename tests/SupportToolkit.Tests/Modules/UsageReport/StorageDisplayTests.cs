using SupportToolkit.Modules.UsageReport.Models;

namespace SupportToolkit.Tests.Modules.UsageReport;

/// <summary>
/// A figure as the backup system's screens show it: for a portal that
/// switches to TB at 1,024 GB (1 TB = 1,024 GB, two decimals), the report
/// shows the same number the portal does. Synthetic figures.
/// </summary>
public class StorageDisplayTests
{
    [Theory]
    [InlineData("841.20", "841.20 GB")]
    [InlineData("1023.99", "1,023.99 GB")]
    [InlineData("1024", "1.00 TB")]
    [InlineData("1300", "1.27 TB")]
    [InlineData("1536", "1.50 TB")]
    [InlineData("3500", "3.42 TB")]
    [InlineData("1029.12", "1.01 TB")]
    public void APortalThatShowsTerabytes_IsMatched(string gb, string shown)
    {
        Assert.Equal(shown, StorageDisplay.AsShown(decimal.Parse(gb, System.Globalization.CultureInfo.InvariantCulture), true));
    }

    [Fact]
    public void ASystemThatShowsGigabytes_StaysInGigabytes()
    {
        Assert.Equal("3,500.00 GB", StorageDisplay.AsShown(3500m, false));
        Assert.False(StorageDisplay.InTerabytes(3500m, false));
    }

    [Fact]
    public void TheSwitch_IsAt1024Gb()
    {
        Assert.False(StorageDisplay.InTerabytes(1023.99m, true));
        Assert.True(StorageDisplay.InTerabytes(1024m, true));
    }
}
