using System.Net;
using SupportToolkit.Core.ErrorHandling;
using SupportToolkit.Providers.BusinessCentral.Transport;
using static SupportToolkit.Tests.Providers.BusinessCentral.BusinessCentralTestSupport;

namespace SupportToolkit.Tests.Providers.BusinessCentral;

public class BusinessCentralApiClientTests
{
    private const string SecretBody =
        "{\"error\":{\"message\":\"customer 12345 secret-detail\"}}";

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task ErrorStatuses_MapToSafeMessages(
        HttpStatusCode status)
    {
        var client = CreateApiClient(
            new StubNetwork().Enqueue(() => Json(SecretBody, status)));

        var exception =
            await Assert.ThrowsAsync<BusinessCentralApiException>(
                () => client.GetJsonAsync<Dummy>(CollectionPath));

        Assert.Equal(status, exception.StatusCode);

        Assert.DoesNotContain("secret-detail", exception.Message);
        Assert.DoesNotContain("12345", exception.Message);
        Assert.DoesNotContain(CompanyId, exception.Message);
        Assert.DoesNotContain(TenantId, exception.Message);

        // Operator output must be the safe message itself.
        Assert.Equal(
            exception.Message,
            ConsoleErrorFormatter.Format(exception));
    }

    [Fact]
    public async Task Throttle_ReportsRetryAfterSeconds()
    {
        var client = CreateApiClient(
            new StubNetwork().Enqueue(() =>
            {
                var response = Json("{}", HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter =
                    new System.Net.Http.Headers.RetryConditionHeaderValue(
                        TimeSpan.FromSeconds(30));
                return response;
            }));

        var exception =
            await Assert.ThrowsAsync<BusinessCentralApiException>(
                () => client.GetJsonAsync<Dummy>(CollectionPath));

        Assert.Contains("30 seconds", exception.Message);
    }

    [Fact]
    public async Task MalformedJson_IsContractFailure()
    {
        var client = CreateApiClient(
            new StubNetwork().Enqueue(() => Json("{not json")));

        await Assert.ThrowsAsync<BusinessCentralContractException>(
            () => client.GetJsonAsync<Dummy>(CollectionPath));
    }

    [Fact]
    public async Task NullJson_IsContractFailure()
    {
        var client = CreateApiClient(
            new StubNetwork().Enqueue(() => Json("null")));

        await Assert.ThrowsAsync<BusinessCentralContractException>(
            () => client.GetJsonAsync<Dummy>(CollectionPath));
    }

    private static BusinessCentralApiClient CreateApiClient(
        StubNetwork network)
    {
        return new BusinessCentralApiClient(
            Client(
                new FakeTokenProvider(),
                network,
                CollectionPath));
    }

    private sealed class Dummy
    {
        public string? Name { get; init; }
    }
}
