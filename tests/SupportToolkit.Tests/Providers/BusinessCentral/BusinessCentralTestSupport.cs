using System.Net;
using System.Text;
using SupportToolkit.Providers.BusinessCentral.Auth;
using SupportToolkit.Providers.BusinessCentral.Transport;

namespace SupportToolkit.Tests.Providers.BusinessCentral;

internal static class BusinessCentralTestSupport
{
    public const string TenantId =
        "11111111-1111-1111-1111-111111111111";

    public const string ClientId =
        "22222222-2222-2222-2222-222222222222";

    public const string CompanyId =
        "33333333-3333-3333-3333-333333333333";

    public const string Token =
        "synthetic-token-value";

    public const string CollectionPath =
        "companies(" + CompanyId + ")/syntheticItems";

    public static BusinessCentralOptions Options()
    {
        return BusinessCentralOptions.Create(
            TenantId,
            ClientId,
            "Sandbox",
            CompanyId,
            "example",
            "monitoring",
            "v1.0"
        );
    }

    public static HttpClient Client(
        FakeTokenProvider tokens,
        StubNetwork network,
        params string[] approvedPaths)
    {
        return BusinessCentralHttpPipeline.Create(
            Options(),
            tokens,
            approvedPaths,
            network
        );
    }

    public static string Link(
        string relativePath)
    {
        return Options().ApiBaseUri + relativePath;
    }

    public static HttpResponseMessage Json(
        string json,
        HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status)
        {
            Content =
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json"
                )
        };
    }

    public sealed class FakeTokenProvider
        : IBusinessCentralTokenProvider
    {
        public int Requests { get; private set; }

        public Task<string> GetAccessTokenAsync(
            CancellationToken cancellationToken = default)
        {
            Requests++;

            return Task.FromResult(Token);
        }
    }

    public sealed class StubNetwork
        : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses =
            new();

        public List<HttpRequestMessage> Requests { get; } =
            new();

        public StubNetwork Enqueue(
            Func<HttpResponseMessage> response)
        {
            _responses.Enqueue(response);

            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return Task.FromResult(
                _responses.Dequeue()()
            );
        }
    }
}
