using SupportToolkit.Modules.ZendeskBulkDelete.Production;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using SupportToolkit.Modules.ZendeskBulkDelete.Models;
using SupportToolkit.Modules.ZendeskBulkDelete.Services;
using SupportToolkit.Providers.Zendesk.Deletion;
using SupportToolkit.Providers.Zendesk.Deletion.Deleting;
using SupportToolkit.Providers.Zendesk.Deletion.Search;
using SupportToolkit.Providers.Zendesk.Deletion.Transport;

namespace SupportToolkit.Tests.Modules.ZendeskBulkDelete;

/// <summary>
/// Fictional data and a fake Zendesk for the deletion tests.
/// </summary>
internal static class DeletionTestSupport
{
    public static readonly DateTimeOffset Now =
        new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    public static readonly DeletionOperator Operator =
        new(7, "Example Operator", "operator@example.com", "agent");

    public static TicketSummary Ticket(
        long id,
        string status = "closed",
        TimeSpan? closedAgo = null) =>
        new(
            id,
            status,
            $"Example subject {id}",
            1,
            10,
            Now.AddDays(-60),
            Now - (closedAgo ?? TimeSpan.FromDays(30)));

    public static DeletionPreviewResult Preview(
        IEnumerable<TicketSummary> tickets,
        bool complete = true)
    {
        var list = tickets.ToArray();

        var search =
            new TicketSearchResult(
                list,
                list.Length,
                0,
                complete,
                complete ? null : "too many matches");

        return new DeletionPreviewResult(
            Operator,
            "type:ticket example",
            DeletionPreviewBuilder.Build(
                search,
                new Dictionary<long, string> { [10] = "Example Org" },
                new Dictionary<long, string> { [1] = "a@example.com" },
                null,
                Now));
    }

    public static IEnumerable<TicketSummary> Closed(int count) =>
        Enumerable.Range(1, count).Select(i => Ticket(i));

    public static DeletionPlan Plan(
        IEnumerable<TicketSummary> tickets,
        params long[] include)
    {
        var preview = Preview(tickets);
        var selection = new DeletionSelection(preview.Preview);

        selection.Select(include);

        return DeletionPlan.Create(preview, selection, "example", Now);
    }

    public static string NewTempDirectory() =>
        Directory.CreateTempSubdirectory("st-delete-receipts-").FullName;

    public static ZendeskTicketDeletionProvider Provider(
        HttpMessageHandler handler) =>
        new(
            new ZendeskDeletionApiClient(
                new HttpClient(handler)
                {
                    BaseAddress = new Uri("https://example.zendesk.com/")
                },
                ZendeskDeletionOptions.Create(
                    "example",
                    "example-public-client")));

    public static ZendeskDeletionExecutor Executor(
        HttpMessageHandler handler,
        string receiptDirectory,
        TimeSpan? maxJobWait = null) =>
        new(
            new ZendeskTicketDeleter(Provider(handler)),
            new DeletionReceiptStore(receiptDirectory),
            new FixedClock(),
            (_, _) => Task.CompletedTask,
            TimeSpan.FromSeconds(2),
            maxJobWait);

    public sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>
    /// A scripted Zendesk for ticket deletion: DELETE starts a queued job,
    /// and each job status poll advances it until it completes.
    /// </summary>
    public sealed class FakeDeleteApi : HttpMessageHandler
    {
        private readonly Dictionary<string, long[]> _jobs = new();
        private readonly Dictionary<string, int> _polls = new();

        /// <summary>Tickets Zendesk reports as not deleted.</summary>
        public Func<long, bool> FailTicket { get; set; } = _ => false;

        /// <summary>Polls before a job completes. 1 = first poll.</summary>
        public int PollsUntilDone { get; set; } = 1;

        public string FinalStatus { get; set; } = "completed";

        /// <summary>Tickets left out of a job's results.</summary>
        public Func<long, bool> OmitResult { get; set; } = _ => false;

        public Func<long[], HttpResponseMessage?>? DeleteOverride
        {
            get;
            set;
        }

        public Func<string, HttpResponseMessage?>? JobOverride
        {
            get;
            set;
        }

        public Action<HttpRequestMessage>? OnRequest { get; set; }

        public List<(string Method, string PathAndQuery)> Calls { get; } = [];

        public List<long[]> DeletedBatches { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            OnRequest?.Invoke(request);

            var uri = request.RequestUri!;

            Calls.Add((request.Method.Method, uri.PathAndQuery));

            if (request.Method == HttpMethod.Delete)
            {
                var ids =
                    HttpUtility.ParseQueryString(uri.Query)["ids"]!
                        .Split(',')
                        .Select(long.Parse)
                        .ToArray();

                DeletedBatches.Add(ids);

                var overridden = DeleteOverride?.Invoke(ids);

                if (overridden is not null)
                {
                    return Task.FromResult(overridden);
                }

                var jobId = $"J{_jobs.Count + 1}";

                _jobs[jobId] = ids;
                _polls[jobId] = 0;

                return Task.FromResult(
                    Json(
                        HttpStatusCode.OK,
                        JobJson(jobId, "queued", ids, withResults: false)));
            }

            var id = uri.AbsolutePath.Split('/').Last().Replace(".json", "");

            var jobOverridden = JobOverride?.Invoke(id);

            if (jobOverridden is not null)
            {
                return Task.FromResult(jobOverridden);
            }

            var polls = ++_polls[id];

            return Task.FromResult(
                polls < PollsUntilDone
                    ? Json(
                        HttpStatusCode.OK,
                        JobJson(id, "working", _jobs[id], withResults: false))
                    : Json(
                        HttpStatusCode.OK,
                        JobJson(id, FinalStatus, _jobs[id], withResults: true)));
        }

        private string JobJson(
            string jobId,
            string status,
            long[] ids,
            bool withResults)
        {
            var results =
                withResults
                    ? ids.Where(id => !OmitResult(id))
                        .Select(
                            id => new
                            {
                                id,
                                action = "destroy",
                                success = !FailTicket(id),
                                status = FailTicket(id) ? "Failed" : "Deleted"
                            })
                        .ToArray()
                    : null;

            return JsonSerializer.Serialize(
                new
                {
                    job_status = new
                    {
                        id = jobId,
                        status,
                        total = ids.Length,
                        progress = withResults ? ids.Length : 0,
                        message = status == "failed" ? "Job failed" : null,
                        results
                    }
                });
        }

        public static HttpResponseMessage Json(
            HttpStatusCode status,
            string json) =>
            new(status)
            {
                Content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json")
            };
    }
}
