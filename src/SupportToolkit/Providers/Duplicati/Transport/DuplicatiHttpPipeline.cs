namespace SupportToolkit.Providers.Duplicati.Transport;

/// <summary>
/// The only place the Duplicati HTTP pipeline is assembled.
///
/// It is read-only by construction: DuplicatiReadOnlyHandler lets through
/// only GET requests under api/v1/ and the one POST that signs in, so a
/// mistake elsewhere cannot start, change or delete a backup. Redirects are
/// not followed, so the password and token are never replayed elsewhere.
/// </summary>
public static class DuplicatiHttpPipeline
{
    private static readonly TimeSpan RequestTimeout =
        TimeSpan.FromSeconds(30);

    public static HttpClient Create(
        HttpMessageHandler? networkHandler = null)
    {
        networkHandler ??=
            new HttpClientHandler
            {
                AllowAutoRedirect = false
            };

        return new HttpClient(
            new DuplicatiReadOnlyHandler { InnerHandler = networkHandler })
        {
            Timeout = RequestTimeout
        };
    }
}

/// <summary>
/// Refuses every request but reads and the sign-in, before it leaves the
/// computer.
/// </summary>
public sealed class DuplicatiReadOnlyHandler : DelegatingHandler
{
    public const string SignInPath = "api/v1/auth/login";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        var allowed =
            (request.Method == HttpMethod.Get
                && path.Contains("/api/v1/", StringComparison.Ordinal))
            || (request.Method == HttpMethod.Post
                && path.EndsWith("/" + SignInPath, StringComparison.Ordinal));

        if (!allowed)
        {
            throw new InvalidOperationException(
                $"The Duplicati connection is read-only; {request.Method} " +
                $"{path} was refused before it was sent.");
        }

        return base.SendAsync(request, cancellationToken);
    }
}
