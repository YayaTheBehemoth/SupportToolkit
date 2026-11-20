using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace SupportToolkit.Tests.Providers.Dolibarr;

/// <summary>
/// A Dolibarr REST API answering like the lab's Dolibarr 23.0.4
/// (lab/dolibarr/README.md), with synthetic records. Contracts and
/// customers are raw JSON objects, so tests can use Dolibarr's loose
/// types exactly as it sends them. It records every request.
/// </summary>
internal sealed class FakeDolibarr : HttpMessageHandler
{
    public const string ApiKey = "EXAMPLE-DOLIBARR-KEY-6c0a";

    public List<JsonObject> Contracts { get; } = [];

    public List<JsonObject> Customers { get; } = [];

    public HttpStatusCode? FailWith { get; set; }

    /// <summary>Answers every page like the first (a server ignoring paging).</summary>
    public bool IgnorePaging { get; set; }

    public List<(HttpMethod Method, string PathAndQuery, string? Key)> Requests { get; } = [];

    public static Uri ApiRoot { get; } = new("https://erp.example.com/api/index.php/");

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var key =
            request.Headers.TryGetValues("DOLAPIKEY", out var values) ? values.Single() : null;

        Requests.Add((request.Method, request.RequestUri!.PathAndQuery, key));

        if (FailWith is { } failure)
        {
            return Task.FromResult(Json(
                """{"error":{"code":500,"message":"failure"},"debug":{"source":"/var/www/html/api/x.php"}}""",
                failure));
        }

        if (key != ApiKey)
        {
            return Task.FromResult(Json(
                """{"error":{"code":401,"message":"Unauthorized: Error user not valid"},"debug":{"source":"x"}}""",
                HttpStatusCode.Unauthorized));
        }

        var path = request.RequestUri.AbsolutePath;
        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
        var limit = int.Parse(query["limit"]!);
        var page = IgnorePaging ? 0 : int.Parse(query["page"]!);

        var source =
            path.EndsWith("/contracts") ? Contracts
            : path.EndsWith("/thirdparties") ? Customers
            : null;

        if (source is null)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        var items = new JsonArray(
            source.Skip(page * limit).Take(limit)
                .Select(item => (JsonNode)item.DeepClone())
                .ToArray());

        return Task.FromResult(Json(items.ToJsonString()));
    }

    public static JsonObject Customer(string id, string name, string code) =>
        new()
        {
            ["id"] = id,
            ["name"] = name,
            ["name_alias"] = "",
            ["code_client"] = code,
            ["client"] = "1",
            ["status"] = "1"
        };

    public static JsonObject Contract(string id, string reference, string customerId, string status, params JsonObject[] lines) =>
        new()
        {
            ["id"] = id,
            ["ref"] = reference,
            ["socid"] = customerId,
            ["statut"] = status,
            ["status"] = status,
            ["date_contrat"] = 1767225600,
            ["lines"] = new JsonArray(lines.Select(line => (JsonNode)line).ToArray())
        };

    /// <summary>A line as the lab returned it; dates are Unix seconds or "".</summary>
    public static JsonObject Line(
        string id,
        string? product,
        string status,
        JsonNode? start = null,
        JsonNode? end = null,
        JsonNode? startReal = null,
        JsonNode? endReal = null,
        string qty = "1") =>
        new()
        {
            ["id"] = id,
            ["fk_product"] = product is null ? null : "1",
            ["product_ref"] = product,
            ["statut"] = status,
            ["status"] = status,
            ["qty"] = qty,
            ["date_start"] = start ?? 1767225600,
            ["date_end"] = end ?? "",
            ["date_start_real"] = startReal ?? (status == "0" ? "" : 1767225600),
            ["date_end_real"] = endReal ?? "",
            ["date_ouverture"] = null,
            ["date_cloture"] = null
        };

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
