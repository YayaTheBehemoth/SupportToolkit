using System.Reflection;
using System.Text.Json.Serialization;
using SupportToolkit.Providers.BusinessCentral.Subscriptions.Dtos;
using SupportToolkit.Providers.BusinessCentral.Transport;
using static SupportToolkit.Tests.Providers.BusinessCentral.BusinessCentralTestSupport;

namespace SupportToolkit.Tests.Providers.BusinessCentral;

public class BusinessCentralSubscriptionDtoTests
{
    private const string HeaderPath =
        "companies(" + CompanyId + ")/subscriptionHeaders";

    private const string LinePath =
        "companies(" + CompanyId + ")/subscriptionLines";

    [Fact]
    public async Task Header_DeserializesSyntheticPage_IgnoringUnrequestedFields()
    {
        const string json = """
            {"value":[{
              "id":"44444444-4444-4444-4444-444444444444",
              "number":"SUB-1",
              "type":"Standard",
              "blocked":" ",
              "sellToCustomerNumber":"CUST-1",
              "billToCustomerNumber":"CUST-2",
              "startDate":"2026-01-01",
              "endDate":"0001-01-01",
              "status":"Active",
              "sellToEmail":"ignored@example.invalid",
              "totalAmount":1.5,
              "lastModifiedDateTime":"2026-10-01T08:00:00Z"
            }]}
            """;

        var header =
            Assert.Single(
                await Fetch<BusinessCentralSubscriptionHeaderDto>(
                    HeaderPath,
                    json));

        Assert.Equal("SUB-1", header.Number);
        Assert.Equal("CUST-1", header.SellToCustomerNumber);
        Assert.Equal("CUST-2", header.BillToCustomerNumber);
        Assert.Equal("Active", header.Status);
        Assert.Equal("2026-01-01", header.StartDate);

        // Sentinel text is preserved verbatim for explicit mapping.
        Assert.Equal("0001-01-01", header.EndDate);
    }

    [Fact]
    public async Task Line_DeserializesSyntheticPage()
    {
        const string json = """
            {"value":[{
              "id":"55555555-5555-5555-5555-555555555555",
              "subscriptionNumber":"SUB-1",
              "lineNumber":10000,
              "type":"Item",
              "number":"ITEM-1",
              "quantity":1,
              "omitLine":false,
              "headerCategory":"X",
              "environment":"Sandbox",
              "company":"Synthetic",
              "unitPrice":99.0
            }]}
            """;

        var line =
            Assert.Single(
                await Fetch<BusinessCentralSubscriptionLineDto>(
                    LinePath,
                    json));

        Assert.Equal("SUB-1", line.SubscriptionNumber);
        Assert.Equal("ITEM-1", line.Number);
        Assert.Equal(10000, line.LineNumber);
        Assert.Equal(1m, line.Quantity);
        Assert.False(line.OmitLine);
    }

    [Fact]
    public async Task MissingAndNullFields_StayNull_NotDefaulted()
    {
        var header =
            Assert.Single(
                await Fetch<BusinessCentralSubscriptionHeaderDto>(
                    HeaderPath,
                    """{"value":[{"id":"x","number":null}]}"""));

        Assert.Null(header.Number);
        Assert.Null(header.Status);
        Assert.Null(header.StartDate);
        Assert.Null(header.SellToCustomerNumber);
    }

    [Theory]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    [InlineData("3", "3")]
    [InlineData("\"All\"", "All")]
    [InlineData("\" \"", " ")]
    public async Task EnumLikeColumns_PreserveAnyScalarRepresentation(
        string jsonValue,
        string expected)
    {
        var header =
            Assert.Single(
                await Fetch<BusinessCentralSubscriptionHeaderDto>(
                    HeaderPath,
                    "{\"value\":[{\"id\":\"x\",\"blocked\":" + jsonValue + "}]}"));

        Assert.Equal(expected, header.Blocked);
    }

    [Theory]
    [InlineData("{\"a\":1}")]
    [InlineData("[1]")]
    public async Task EnumLikeColumns_RejectStructuredValues(
        string jsonValue)
    {
        await Assert.ThrowsAsync<BusinessCentralContractException>(
            () => Fetch<BusinessCentralSubscriptionHeaderDto>(
                HeaderPath,
                "{\"value\":[{\"id\":\"x\",\"blocked\":" + jsonValue + "}]}"));
    }

    [Fact]
    public async Task NonStringEnumRepresentation_FailsTheContract()
    {
        await Assert.ThrowsAsync<BusinessCentralContractException>(
            () => Fetch<BusinessCentralSubscriptionHeaderDto>(
                HeaderPath,
                """{"value":[{"id":"x","status":3}]}"""));
    }

    [Theory]
    [InlineData(typeof(BusinessCentralSubscriptionHeaderDto),
        BusinessCentralSubscriptionFields.HeaderSelect)]
    [InlineData(typeof(BusinessCentralSubscriptionLineDto),
        BusinessCentralSubscriptionFields.LineSelect)]
    public void SelectLists_MatchDtoProperties_Exactly(
        Type dto,
        string select)
    {
        var modeled =
            dto.GetProperties()
                .Select(p =>
                    p.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name)
                .OrderBy(n => n, StringComparer.Ordinal);

        var selected =
            select.Split(',')
                .OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(modeled, selected);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("unitPrice")]
    [InlineData("totalAmount")]
    [InlineData("sellToEmail")]
    [InlineData("sellToContactName")]
    public void Select_ExcludesSensitiveColumns(
        string column)
    {
        var all =
            (BusinessCentralSubscriptionFields.HeaderSelect
                + ","
                + BusinessCentralSubscriptionFields.LineSelect)
            .Split(',');

        Assert.DoesNotContain(column, all);
    }

    private static Task<IReadOnlyList<T>> Fetch<T>(
        string path,
        string json)
    {
        var network =
            new StubNetwork()
                .Enqueue(() => Json(json));

        var client =
            new BusinessCentralApiClient(
                Client(
                    new FakeTokenProvider(),
                    network,
                    path));

        return new BusinessCentralODataPaginator(client)
            .FetchAllAsync<T>("synthetic", path);
    }
}
