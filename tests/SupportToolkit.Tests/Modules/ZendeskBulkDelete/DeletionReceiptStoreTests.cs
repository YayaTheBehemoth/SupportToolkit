using SupportToolkit.Modules.ZendeskBulkDelete.Models;
using SupportToolkit.Modules.ZendeskBulkDelete.Services;
using static SupportToolkit.Tests.Modules.ZendeskBulkDelete.DeletionTestSupport;

namespace SupportToolkit.Tests.Modules.ZendeskBulkDelete;

public class DeletionReceiptStoreTests
{
    [Fact]
    public void AFolderInsideAGitWorkingTree_IsRefused()
    {
        var repo = Directory.CreateTempSubdirectory("st-fake-repo-");

        try
        {
            Directory.CreateDirectory(Path.Combine(repo.FullName, ".git"));

            var nested = Path.Combine(repo.FullName, "docs", "receipts");

            var exception =
                Assert.Throws<InvalidOperationException>(
                    () => new DeletionReceiptStore(nested));

            Assert.Contains("git working tree", exception.Message);
        }
        finally
        {
            repo.Delete(recursive: true);
        }
    }

    [Fact]
    public void ARepositoryWithAGitFile_IsAlsoRefused()
    {
        // A worktree or submodule has a .git file, not a folder.
        var repo = Directory.CreateTempSubdirectory("st-fake-repo-");

        try
        {
            File.WriteAllText(Path.Combine(repo.FullName, ".git"), "gitdir: x");

            Assert.Throws<InvalidOperationException>(
                () => new DeletionReceiptStore(
                    Path.Combine(repo.FullName, "receipts")));
        }
        finally
        {
            repo.Delete(recursive: true);
        }
    }

    [Fact]
    public void TheDefaultFolder_IsOutsideAnyRepository_AndAccepted()
    {
        var store =
            new DeletionReceiptStore(DeletionReceiptStore.DefaultDirectory);

        Assert.EndsWith(
            Path.Combine("SupportToolkit", "receipts"),
            store.Directory);
    }

    [Fact]
    public async Task Write_CreatesTheFolder_AndOverwritesTheSameFile()
    {
        var root = Directory.CreateTempSubdirectory("st-receipts-");

        try
        {
            var store =
                new DeletionReceiptStore(
                    Path.Combine(root.FullName, "nested", "receipts"));

            var plan = Plan(Closed(2));
            var confirmation = new DeletionConfirmation(2, null);
            var path = store.PathFor(plan.Id, Now);

            await store.WriteAsync(
                path,
                DeletionReceipt.From(
                    plan,
                    confirmation,
                    Now,
                    null,
                    plan.Tickets
                        .Select(
                            t => new TicketDeletionResult(
                                t.Id,
                                TicketDeletionState.NotAttempted,
                                null,
                                null))
                        .ToArray()));

            Assert.Contains("\"started\"", File.ReadAllText(path));

            await store.WriteAsync(
                path,
                DeletionReceipt.From(
                    plan,
                    confirmation,
                    Now,
                    Now.AddMinutes(1),
                    plan.Tickets
                        .Select(
                            t => new TicketDeletionResult(
                                t.Id,
                                TicketDeletionState.Deleted,
                                null,
                                "J1"))
                        .ToArray()));

            var text = File.ReadAllText(path);

            Assert.Contains("\"finished\"", text);
            Assert.DoesNotContain("\"started\"", text);

            // One file, and no temporary file left behind.
            Assert.Equal(
                [path],
                Directory.GetFiles(store.Directory));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void TheFileName_HasTheTimeAndAPartOfTheRunId_AndNoCustomerData()
    {
        var store = new DeletionReceiptStore(NewTempDirectory());

        try
        {
            var id = Guid.Parse("11111111-2222-3333-4444-555555555555");

            var name = Path.GetFileName(store.PathFor(id, Now));

            Assert.Equal("zendesk-delete-20261103-120000-11111111.json", name);
        }
        finally
        {
            Directory.Delete(store.Directory, recursive: true);
        }
    }
}
