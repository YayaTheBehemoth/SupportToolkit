namespace SupportToolkit.Ui.Web;

/// <summary>
/// Parses the arguments after `serve`: [--port &lt;n&gt;] [--no-browser].
/// </summary>
public static class WebServeArguments
{
    public static bool TryParse(
        IReadOnlyList<string> args,
        out WebServeOptions? options,
        out string error)
    {
        options = null;
        error =
            "Usage: SupportToolkit serve [--port <1-65535>] [--no-browser]";

        var port = 0;
        var openBrowser = true;
        var portSeen = false;
        var browserSeen = false;

        for (var index = 0; index < args.Count; index++)
        {
            if (string.Equals(
                    args[index],
                    "--no-browser",
                    StringComparison.OrdinalIgnoreCase)
                && !browserSeen)
            {
                openBrowser = false;
                browserSeen = true;
            }
            else if (string.Equals(
                         args[index],
                         "--port",
                         StringComparison.OrdinalIgnoreCase)
                     && !portSeen
                     && index + 1 < args.Count
                     && int.TryParse(args[index + 1], out port)
                     && port is >= 1 and <= 65535)
            {
                portSeen = true;
                index++;
            }
            else
            {
                return false;
            }
        }

        options = new WebServeOptions(port, openBrowser);
        error = string.Empty;

        return true;
    }
}
