namespace SupportToolkit.Ui.Web;

/// <summary>
/// Whether the pages show their technical detail. The simple view (the
/// default) hides detail such as ticket ID lists, request counts and job
/// progress; it never hides a warning, the Zendesk instance, a count, a
/// typed confirmation, a failed or unknown outcome or the receipt.
///
/// One setting for the single operator, kept until the app exits.
/// </summary>
public sealed class ViewSettings
{
    public event Action? Changed;

    public bool IsDetailed { get; private set; }

    public void Set(bool detailed)
    {
        if (IsDetailed == detailed)
        {
            return;
        }

        IsDetailed = detailed;
        Changed?.Invoke();
    }
}
