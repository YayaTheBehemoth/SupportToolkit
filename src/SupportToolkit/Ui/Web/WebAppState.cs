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
        ConfigurationStatusService status)
    {
        Status =
            status ?? throw new ArgumentNullException(nameof(status));
    }

    public ConfigurationStatusService Status { get; }
}
