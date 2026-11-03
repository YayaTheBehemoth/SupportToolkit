using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SupportToolkit.Modules.ZendeskBulkDelete.Models;

namespace SupportToolkit.Modules.ZendeskBulkDelete.Services;

/// <summary>
/// Writes deletion receipts as JSON files in a local folder.
///
/// A receipt lists ticket IDs and outcomes, but it still identifies a
/// customer's tickets and the operator, and this repository is public. The
/// folder is therefore refused when it lies inside any git working tree.
/// </summary>
public sealed class DeletionReceiptStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };

    public DeletionReceiptStore(
        string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        Directory = Path.GetFullPath(directory);

        RefuseGitWorkingTree(Directory);
    }

    /// <summary>
    /// The default folder: the user's local application data, outside
    /// any repository.
    /// </summary>
    public static string DefaultDirectory =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "SupportToolkit",
            "receipts");

    public string Directory { get; }

    /// <summary>
    /// One file per run, so the receipt written before deletion and the
    /// final one are the same file.
    /// </summary>
    public string PathFor(
        Guid runId,
        DateTimeOffset startedAt) =>
        Path.Combine(
            Directory,
            "zendesk-delete-" +
            startedAt.UtcDateTime.ToString("yyyyMMdd-HHmmss") +
            "-" +
            runId.ToString("N")[..8] +
            ".json");

    /// <summary>
    /// Writes the receipt through a temporary file, so a failed write
    /// never leaves a half-written receipt in place of a good one.
    /// </summary>
    public async Task WriteAsync(
        string path,
        DeletionReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(receipt);

        System.IO.Directory.CreateDirectory(Directory);

        var temporary = path + ".tmp";

        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(receipt, JsonOptions),
            new UTF8Encoding(false),
            cancellationToken);

        File.Move(temporary, path, overwrite: true);
    }

    private static void RefuseGitWorkingTree(
        string directory)
    {
        for (var ancestor = directory;
             ancestor is not null;
             ancestor = Path.GetDirectoryName(ancestor))
        {
            var marker = Path.Combine(ancestor, ".git");

            if (System.IO.Directory.Exists(marker) || File.Exists(marker))
            {
                throw new InvalidOperationException(
                    "Refusing to keep deletion receipts inside a git " +
                    "working tree: they identify customer tickets and " +
                    "this repository is public.");
            }
        }
    }
}
