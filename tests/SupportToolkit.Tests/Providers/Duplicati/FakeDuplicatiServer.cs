using System.Net;
using System.Text;
using System.Text.Json;

namespace SupportToolkit.Tests.Providers.Duplicati;

/// <summary>
/// A Duplicati server answering like the lab's Duplicati 2.4.1.0
/// (lab/duplicati/README.md), with synthetic jobs. It records every
/// request it is sent.
/// </summary>
internal sealed class FakeDuplicatiServer : HttpMessageHandler
{
    public const string Password = "EXAMPLE-DUPLICATI-PASSWORD-3b7d";
    public const string Token = "EXAMPLE-ACCESS-TOKEN-91fe";

    /// <summary>Credentials in a destination URL, as real ones can carry.</summary>
    public const string TargetSecret = "EXAMPLE-TARGET-SECRET-55aa";

    private readonly List<Job> _jobs = [];

    public string? RunningJobId { get; set; }

    public HttpStatusCode? FailEverythingWith { get; set; }

    public List<(HttpMethod Method, string Path, string? Authorization)> Requests { get; } = [];

    public sealed record Run(string Result, string EndTime, bool Interrupted = false, string Operation = "Backup");

    private sealed record Job(
        string Id,
        string Name,
        Dictionary<string, string> Metadata,
        IReadOnlyList<Run> RunsNewestFirst);

    public FakeDuplicatiServer AddJob(
        string id,
        string name,
        Dictionary<string, string>? metadata = null,
        params Run[] runsNewestFirst)
    {
        _jobs.Add(new Job(id, name, metadata ?? [], runsNewestFirst));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;

        Requests.Add((
            request.Method,
            request.RequestUri.PathAndQuery,
            request.Headers.Authorization?.ToString()));

        if (FailEverythingWith is { } failure)
        {
            return new HttpResponseMessage(failure);
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/api/v1/auth/login"))
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var password = JsonDocument.Parse(body).RootElement.GetProperty("Password").GetString();

            return password == Password
                ? Json($$"""{"AccessToken":"{{Token}}","RefreshNonce":"x"}""")
                : Json("""{"Error":"Failed to log in","Code":401}""", HttpStatusCode.Unauthorized);
        }

        if (request.Headers.Authorization?.ToString() != $"Bearer {Token}")
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        if (path.EndsWith("/api/v1/serverstate"))
        {
            var active =
                RunningJobId is null
                    ? "null"
                    : $$"""{"Item1":3,"Item2":"{{RunningJobId}}"}""";

            return Json($$"""{"ActiveTask":{{active}},"ProgramState":"Running","HasWarning":false,"HasError":false}""");
        }

        if (path.EndsWith("/api/v1/backups"))
        {
            var jobs =
                _jobs.Select(job => new
                {
                    Backup = new
                    {
                        ID = job.Id,
                        job.Name,
                        TargetURL = $"ssh://backup.example.com/{job.Id}?auth-password={TargetSecret}",
                        Metadata = job.Metadata
                    },
                    Schedule = (object?)null
                });

            return Json(JsonSerializer.Serialize(jobs));
        }

        var logMarker = "/api/v1/backup/";

        if (path.Contains(logMarker) && path.EndsWith("/log"))
        {
            var id = path[(path.IndexOf(logMarker) + logMarker.Length)..^"/log".Length];
            var job = _jobs.Single(candidate => candidate.Id == id);

            var entries =
                job.RunsNewestFirst.Select((run, index) => new
                {
                    ID = job.RunsNewestFirst.Count - index,
                    OperationID = 1,
                    Timestamp = 1795175524,
                    Type = "Result",
                    Message = JsonSerializer.Serialize(new
                    {
                        MainOperation = run.Operation,
                        ParsedResult = run.Result,
                        run.Interrupted,
                        BeginTime = run.EndTime,
                        run.EndTime,
                        Errors = new[] { "2026-11-20 11:55:23 +00 - [Error] /home/someone/secret-path failed" }
                    })
                });

            return Json(JsonSerializer.Serialize(entries));
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
