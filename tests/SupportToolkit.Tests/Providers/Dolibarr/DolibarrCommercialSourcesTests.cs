using System.Net;
using System.Text.Json.Nodes;
using SupportToolkit.Domain.MonitoringScope;
using SupportToolkit.Providers.Dolibarr.Configuration;
using SupportToolkit.Providers.Dolibarr.Subscriptions;
using SupportToolkit.Providers.Dolibarr.Transport;
using static SupportToolkit.Tests.Providers.Dolibarr.FakeDolibarr;

namespace SupportToolkit.Tests.Providers.Dolibarr;

/// <summary>
/// Dolibarr contracts as subscription evidence, one subscription per line,
/// from responses shaped like the lab's. All records are synthetic.
/// </summary>
public class DolibarrCommercialSourcesTests
{
    private static readonly ResolvedDolibarrConnection Connection = new("lab", ApiRoot, ApiKey);

    private static readonly DateOnly January = new(2026, 1, 1);

    /// <summary>The lab's records (lab/dolibarr/README.md).</summary>
    private static FakeDolibarr Lab()
    {
        var dolibarr = new FakeDolibarr();

        dolibarr.Customers.Add(Customer("1", "Northwind Traders ApS", "CU2611-00001"));
        dolibarr.Customers.Add(Customer("2", "Contoso A/S", "CU2611-00002"));
        dolibarr.Customers.Add(Customer("3", "Fabrikam", "CU2611-00003"));

        dolibarr.Contracts.Add(Contract("1", "CT2601-0001", "1", "1",
            Line("1", "EXAMPLE-MONITORING", "4"),
            Line("2", "EXAMPLE-BACKUP", "4", qty: "250")));
        dolibarr.Contracts.Add(Contract("2", "CT2601-0002", "2", "1",
            Line("3", "EXAMPLE-BACKUP", "4", qty: "100"),
            Line("4", "EXAMPLE-MONITORING", "5", endReal: 1795178176)));
        dolibarr.Contracts.Add(Contract("3", "CT2601-0003", "3", "1",
            Line("5", "EXAMPLE-MONITORING", "0")));

        return dolibarr;
    }

    private static Task<SmartSubscriptionEvidence> EvidenceAsync(FakeDolibarr dolibarr) =>
        new DolibarrSubscriptionEvidenceSource(Connection, () => DolibarrHttpPipeline.Create(dolibarr))
            .GetEvidenceAsync();

    [Fact]
    public async Task EachContractLine_IsOneSubscription_WithItsOwnStatusAndDates()
    {
        var evidence = await EvidenceAsync(Lab());

        Assert.Equal(
            [
                new SmartSubscriptionHeader("CT2601-0001/1", "CU2611-00001", "Active", January, null, null, " "),
                new SmartSubscriptionHeader("CT2601-0001/2", "CU2611-00001", "Active", January, null, null, " "),
                new SmartSubscriptionHeader("CT2601-0002/3", "CU2611-00002", "Active", January, null, null, " "),
                new SmartSubscriptionHeader("CT2601-0002/4", "CU2611-00002", "Suspended", January, new DateOnly(2026, 11, 20), null, " "),
                new SmartSubscriptionHeader("CT2601-0003/5", "CU2611-00003", "Suspended", January, null, null, " ")
            ],
            evidence.Headers);

        Assert.Equal(
            new SmartSubscriptionLine("CT2601-0001/2", "EXAMPLE-BACKUP", "Item", false, 250m),
            evidence.Lines[1]);
    }

    [Fact]
    public async Task TheLabRecords_EntitleOnlyTheRunningMonitoringLine()
    {
        var evidence = await EvidenceAsync(Lab());

        var observations =
            new SmartSubscriptionEntitlementEvaluator("EXAMPLE-MONITORING")
                .Evaluate(evidence.Headers, evidence.Lines, new DateOnly(2026, 11, 21));

        var entitled =
            observations
                .Where(observation => observation.Assessment == MonitoringEntitlementAssessment.ActiveCandidate)
                .Select(observation => observation.CustomerNumber ?? "(none)")
                .Distinct()
                .ToArray();

        Assert.Equal(["CU2611-00001"], entitled);
    }

    [Fact]
    public async Task ARunningLinePastItsPlannedEnd_EndsThen()
    {
        var dolibarr = Lab();
        dolibarr.Contracts[0]["lines"]![0]!["date_end"] = 1793491200; // 2026-11-01

        var evidence = await EvidenceAsync(dolibarr);

        Assert.Equal(new DateOnly(2026, 11, 1), evidence.Headers[0].EndDate);
    }

    [Theory]
    [InlineData("0", "4")]
    [InlineData("2", "4")]
    [InlineData("1", "0")]
    [InlineData("1", "5")]
    public async Task ADraftOrClosedContract_OrAnInactiveLine_IsSuspended(string contract, string line)
    {
        var dolibarr = new FakeDolibarr();
        dolibarr.Contracts.Add(Contract("1", "CT-1", "1", contract, Line("1", "EXAMPLE-MONITORING", line)));

        Assert.Equal("Suspended", Assert.Single((await EvidenceAsync(dolibarr)).Headers).Status);
    }

    [Theory]
    [InlineData("1", "9")]
    [InlineData("7", "4")]
    public async Task AnUnknownStatus_IsPassedOnUnrecognized_NotGuessed(string contract, string line)
    {
        var dolibarr = new FakeDolibarr();
        dolibarr.Contracts.Add(Contract("1", "CT-1", "1", contract, Line("1", "EXAMPLE-MONITORING", line)));

        var status = Assert.Single((await EvidenceAsync(dolibarr)).Headers).Status;

        Assert.Equal(
            SubscriptionStatusState.Unrecognized,
            SmartSubscriptionStates.ParseStatus(status, SmartSubscriptionStatusVocabulary.English));
    }

    [Fact]
    public async Task LooseTypes_AreRead_NumbersOrStrings_EmptyOrNullDates()
    {
        var dolibarr = new FakeDolibarr();
        dolibarr.Customers.Add(new JsonObject { ["id"] = 1, ["name"] = "Example", ["code_client"] = "CU-1" });

        var line = Line("1", "EXAMPLE-MONITORING", "4", start: "1767225600", end: JsonValue.Create((string?)null), startReal: "");
        line["id"] = 1;
        line["statut"] = 4;
        line["qty"] = 2.5;

        var contract = Contract("1", "CT-1", "1", "1", line);
        contract["socid"] = 1;
        contract["statut"] = 1;
        dolibarr.Contracts.Add(contract);

        var evidence = await EvidenceAsync(dolibarr);

        Assert.Equal(
            new SmartSubscriptionHeader("CT-1/1", "CU-1", "Active", January, null, null, " "),
            Assert.Single(evidence.Headers));
        Assert.Equal(2.5m, Assert.Single(evidence.Lines).Quantity);
    }

    [Theory]
    [InlineData("2026-01-01")]
    [InlineData("-5")]
    public async Task AnUnreadableDate_FailsTheEvidence_NeverReadAsNoDate(string date)
    {
        var dolibarr = new FakeDolibarr();
        dolibarr.Contracts.Add(Contract("1", "CT-1", "1", "1", Line("1", "EXAMPLE-MONITORING", "4", endReal: date)));

        var error = await Assert.ThrowsAsync<DolibarrApiException>(() => EvidenceAsync(dolibarr));

        Assert.Contains("could not be read", error.Message);
        Assert.DoesNotContain("CT-1", error.Message);
    }

    [Fact]
    public async Task AFreeTextLine_EntitlesNothing_AndIsLeftOut()
    {
        var dolibarr = new FakeDolibarr();
        dolibarr.Contracts.Add(Contract("1", "CT-1", "1", "1",
            Line("1", null, "4"),
            Line("2", "EXAMPLE-MONITORING", "4")));

        Assert.Equal("CT-1/2", Assert.Single((await EvidenceAsync(dolibarr)).Headers).SubscriptionNumber);
    }

    [Fact]
    public async Task AContractWhoseCustomerIsUnknown_HasNoCustomerNumber()
    {
        var dolibarr = new FakeDolibarr();
        dolibarr.Contracts.Add(Contract("1", "CT-1", "99", "1", Line("1", "EXAMPLE-MONITORING", "4")));

        Assert.Null(Assert.Single((await EvidenceAsync(dolibarr)).Headers).CustomerNumber);
    }

    // ---------------------------------------------------------------
    // Reading
    // ---------------------------------------------------------------

    [Fact]
    public async Task EveryPageIsRead_UntilAShortOne()
    {
        var dolibarr = new FakeDolibarr();

        for (var index = 1; index <= DolibarrApiClient.PageSize + 1; index++)
        {
            dolibarr.Contracts.Add(Contract($"{index}", $"CT-{index}", "1", "1", Line($"{index}", "EXAMPLE-MONITORING", "4")));
        }

        var evidence = await EvidenceAsync(dolibarr);

        Assert.Equal(DolibarrApiClient.PageSize + 1, evidence.Headers.Count);
        Assert.Contains(dolibarr.Requests, request => request.PathAndQuery.Contains("contracts?limit=100&page=1"));
    }

    [Fact]
    public async Task AServerThatIgnoresPaging_IsRefused_NotReadForever()
    {
        var dolibarr = new FakeDolibarr { IgnorePaging = true };

        for (var index = 1; index <= DolibarrApiClient.PageSize; index++)
        {
            dolibarr.Contracts.Add(Contract($"{index}", $"CT-{index}", "1", "1"));
        }

        var error = await Assert.ThrowsAsync<DolibarrApiException>(() => EvidenceAsync(dolibarr));

        Assert.Contains("same one twice", error.Message);
    }

    [Fact]
    public async Task OnlyGetsAreSent_WithTheKeyHeader_ToContractsAndCustomers()
    {
        var dolibarr = Lab();

        await EvidenceAsync(dolibarr);

        Assert.All(dolibarr.Requests, request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(ApiKey, request.Key);
        });
        Assert.Contains(dolibarr.Requests, request => request.PathAndQuery.StartsWith("/api/index.php/contracts?"));
        Assert.Contains(dolibarr.Requests, request => request.PathAndQuery.StartsWith("/api/index.php/thirdparties?") && request.PathAndQuery.Contains("mode=1"));
    }

    [Fact]
    public async Task ARefusedKey_SaysSo_WithoutTheKeyOrTheBody()
    {
        var error =
            await Assert.ThrowsAsync<DolibarrApiException>(
                () => new DolibarrSubscriptionEvidenceSource(
                        Connection with { ApiKey = "EXAMPLE-WRONG-KEY" },
                        () => DolibarrHttpPipeline.Create(Lab()))
                    .GetEvidenceAsync());

        Assert.Contains("refused the API key (HTTP 401)", error.Message);
        Assert.DoesNotContain("EXAMPLE-WRONG-KEY", error.Message);
        Assert.DoesNotContain("Unauthorized: Error", error.Message);
    }

    [Fact]
    public async Task AServerError_FailsTheEvidence_WithoutTheBody()
    {
        var error =
            await Assert.ThrowsAsync<DolibarrApiException>(
                () => EvidenceAsync(new FakeDolibarr { FailWith = HttpStatusCode.InternalServerError }));

        Assert.Contains("HTTP 500", error.Message);
        Assert.DoesNotContain("/var/www", error.Message);
    }

    [Theory]
    [InlineData("POST", "https://erp.example.com/api/index.php/contracts")]
    [InlineData("PUT", "https://erp.example.com/api/index.php/contracts/1/lines/1/activate")]
    [InlineData("DELETE", "https://erp.example.com/api/index.php/thirdparties/1")]
    [InlineData("GET", "https://erp.example.com/admin/index.php")]
    public async Task ThePipeline_RefusesAnyWrite_BeforeItIsSent(string method, string url)
    {
        var dolibarr = new FakeDolibarr();
        using var client = DolibarrHttpPipeline.Create(dolibarr);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url)));

        Assert.Empty(dolibarr.Requests);
    }

    // ---------------------------------------------------------------
    // Customer names
    // ---------------------------------------------------------------

    [Fact]
    public async Task CustomerNames_AreReadByCustomerCode()
    {
        var names =
            await new DolibarrCustomerNameSource(Connection, () => DolibarrHttpPipeline.Create(Lab()))
                .GetCustomerPartiesAsync(["CU2611-00002", "CU2611-00001", "CU-MISSING"]);

        Assert.Equal(
            ["CU-MISSING", "CU2611-00001", "CU2611-00002"],
            names.Parties.Select(party => party.Id));
        Assert.Equal("Northwind Traders ApS", names.Parties[1].Name);
        Assert.Equal(1, names.CustomersWithoutName);
        Assert.Null(names.Failure);
    }

    [Fact]
    public async Task ACodeHeldByTwoNames_KeepsNoName()
    {
        var dolibarr = Lab();
        dolibarr.Customers.Add(Customer("4", "Someone Else", "CU2611-00001"));

        var names =
            await new DolibarrCustomerNameSource(Connection, () => DolibarrHttpPipeline.Create(dolibarr))
                .GetCustomerPartiesAsync(["CU2611-00001"]);

        Assert.Null(Assert.Single(names.Parties).Name);
        Assert.Equal(1, names.CustomersWithConflictingNames);
    }

    [Fact]
    public async Task AFailedNameRead_DegradesMatching_ButStillListsTheCustomers()
    {
        var names =
            await new DolibarrCustomerNameSource(
                    Connection,
                    () => DolibarrHttpPipeline.Create(new FakeDolibarr { FailWith = HttpStatusCode.BadGateway }))
                .GetCustomerPartiesAsync(["CU2611-00001"]);

        Assert.Null(Assert.Single(names.Parties).Name);
        Assert.Contains("HTTP 502", names.Failure);
    }

    [Theory]
    [InlineData("https://erp.example.com", "https://erp.example.com/api/index.php/")]
    [InlineData("https://www.example.com/erp/", "https://www.example.com/erp/api/index.php/")]
    [InlineData("http://127.0.0.1:8090", "http://127.0.0.1:8090/api/index.php/")]
    public void TheApiRoot_IsBelowTheDolibarrRoot(string baseUrl, string expected)
    {
        Assert.Equal(expected, DolibarrOptions.ParseApiRoot(baseUrl, "lab").ToString());
    }

    [Theory]
    [InlineData("http://erp.example.com/")]
    [InlineData("https://user:pass@erp.example.com/")]
    [InlineData("")]
    public void AnUnsafeOrMissingUrl_IsRefused(string baseUrl)
    {
        Assert.Throws<InvalidOperationException>(() => DolibarrOptions.ParseApiRoot(baseUrl, "lab"));
    }
}
