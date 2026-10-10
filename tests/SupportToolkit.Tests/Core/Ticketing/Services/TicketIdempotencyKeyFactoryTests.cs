using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Core.Ticketing.Services;

namespace SupportToolkit.Tests.Core.Ticketing.Services;

public class TicketIdempotencyKeyFactoryTests
{
    [Fact]
    public void Create_SameSourceAndDraft_ProducesSameKey()
    {
        var draft =
            new TicketDraft
            {
                Subject =
                    "Example ticket",

                Body =
                    "Example ticket body."
            };

        var first =
            TicketIdempotencyKeyFactory.Create(
                "backup-review",
                draft
            );

        var second =
            TicketIdempotencyKeyFactory.Create(
                "backup-review",
                draft
            );

        Assert.Equal(
            first,
            second
        );
    }

    [Fact]
    public void Create_DifferentDraftContent_ProducesDifferentKey()
    {
        var first =
            TicketIdempotencyKeyFactory.Create(
                "backup-review",
                new TicketDraft
                {
                    Subject =
                        "Example ticket",

                    Body =
                        "First body."
                }
            );

        var second =
            TicketIdempotencyKeyFactory.Create(
                "backup-review",
                new TicketDraft
                {
                    Subject =
                        "Example ticket",

                    Body =
                        "Second body."
                }
            );

        Assert.NotEqual(
            first,
            second
        );
    }

    [Fact]
    public void Create_IncludesNormalizedSourceIdentifier()
    {
        var key =
            TicketIdempotencyKeyFactory.Create(
                "Backup Review",
                new TicketDraft
                {
                    Subject =
                        "Example ticket",

                    Body =
                        "Example body."
                }
            );

        Assert.StartsWith(
            "supporttoolkit-backup-review-",
            key
        );
    }
}