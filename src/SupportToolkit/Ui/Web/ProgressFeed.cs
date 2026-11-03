namespace SupportToolkit.Ui.Web;

/// <summary>
/// Progress messages from a running review, for the page to show. Reviews
/// run tenants concurrently, so writes are serialized. Only the latest
/// messages are kept.
/// </summary>
public sealed class ProgressFeed
{
    private const int MaxMessages = 200;

    private readonly object _lock = new();
    private readonly List<string> _messages = [];

    public event Action? Changed;

    public void Write(string message)
    {
        lock (_lock)
        {
            _messages.Add(message);

            if (_messages.Count > MaxMessages)
            {
                _messages.RemoveRange(0, _messages.Count - MaxMessages);
            }
        }

        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _messages.Clear();
        }

        Changed?.Invoke();
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_lock)
        {
            return _messages.ToArray();
        }
    }
}
