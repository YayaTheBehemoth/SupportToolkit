using SupportToolkit.Composition;
using SupportToolkit.Core.Configuration;

namespace SupportToolkit.Ui.Web;

/// <summary>
/// The one operator's state for the life of the web UI process. It is a
/// singleton: SupportToolkit is a single-operator tool, so two browser tabs
/// show the same state by design.
///
/// It holds services and sessions only. Pages render what they return and
/// contain no rules of their own.
/// </summary>
public sealed class WebAppState
{
    public WebAppState(
        ConfigurationStatusService status,
        AggregatorWebSession? aggregator = null,
        BulkDeleteWebSession? bulkDelete = null,
        UsageReportWebSession? usageReport = null)
    {
        Status =
            status ?? throw new ArgumentNullException(nameof(status));

        Aggregator = aggregator;
        BulkDelete = bulkDelete;
        UsageReport = usageReport;
    }

    public ViewSettings View { get; } = new();

    public ConfigurationStatusService Status { get; }

    /// <summary>
    /// The Backup Aggregator review, or null in a host that has none.
    /// </summary>
    public AggregatorWebSession? Aggregator { get; }

    /// <summary>
    /// The Zendesk bulk delete, or null in a host that has none.
    /// </summary>
    public BulkDeleteWebSession? BulkDelete { get; }

    /// <summary>
    /// The usage report, or null in a host that has none.
    /// </summary>
    public UsageReportWebSession? UsageReport { get; }
}
