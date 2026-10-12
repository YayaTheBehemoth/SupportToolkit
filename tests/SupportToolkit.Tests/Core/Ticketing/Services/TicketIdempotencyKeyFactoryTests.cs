
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Core.Ticketing.Services;

namespace SupportToolkit.Tests.Core.Ticketing.Services;

public class TicketIdempotencyKeyFactoryTests
{
    [Fact]
    public void Create_SameSourceDraftAndDestination_ProducesSameKey()
    {
        var draft =
            new TicketDraft
            {
                Subject = "Example ticket",
                Body = "Example ticket body."
            };

        var first =
            TicketIdempotencyKeyFactory.Create(
                "backup-review",
                draft,
                "zendesk:fixture:111111"
            );

        var second =
            TicketIdempotencyKeyFactory.Create(
                "backup-review",
                draft,
                "zendesk:fixture:111111"
            );

        Assert.Equal(first, second);
    }

    [Fact]
    public void Create_DifferentDraftContent_ProducesDifferentKey()
    {
        var first =
            TicketIdempotencyKeyFactory.Create(
                "backup-review",
                new TicketDraft
                {
                    Subject = "Example ticket",
                    Body = "First body."
                },
                "zendesk:fixture:111111"
            );

        var second =
            TicketIdempotencyKeyFactory.Create(
                "backup-review",
                new TicketDraft
                {
                    Subject = "Example ticket",
                    Body = "Second body."
                },
                "zendesk:fixture:111111"
            );

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Create_DifferentGroup_ProducesDifferentKey()
    {
        var draft =
            new TicketDraft
            {
                Subject = "Example ticket",
                Body = "Example ticket body."
            };

        var devKey =
            TicketIdempotencyKeyFactory.Create(
                "backup-review",
                draft,
                "zendesk:fixture:111111"
            );

        var operationsKey =
            TicketIdempotencyKeyFactory.Create(
                "backup-review",
                draft,
                "zendesk:fixture:222222"
            );

        Assert.NotEqual(devKey, operationsKey);
    }

    [Fact]
    public void Create_DifferentInstance_ProducesDifferentKey()
    {
        var draft =
            new TicketDraft
            {
                Subject = "Example ticket",
                Body = "Example ticket body."
            };

        var first =
            TicketIdempotencyKeyFactory.Create(
                "backup-review",
                draft,
                "zendesk:instance-one:111111"
            );

        var second =
            TicketIdempotencyKeyFactory.Create(
                "backup-review",
                draft,
                "zendesk:instance-two:111111"
            );

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Create_DestinationIdentity_IsCaseInsensitive()
    {
        var draft =
            new TicketDraft
            {
                Subject = "Example ticket",
                Body = "Example ticket body."
            };

        var first =
            TicketIdempotencyKeyFactory.Create(
                "backup-review",
                draft,
                "ZENDESK:FIXTURE:111111"
            );

        var second =
            TicketIdempotencyKeyFactory.Create(
                "backup-review",
                draft,
                "zendesk:fixture:111111"
            );

        Assert.Equal(first, second);
    }

    [Fact]
    public void Create_IncludesNormalizedSourceIdentifier()
    {
        var key =
            TicketIdempotencyKeyFactory.Create(
                "Backup Review",
                new TicketDraft
                {
                    Subject = "Example ticket",
                    Body = "Example body."
                },
                "zendesk:fixture:111111"
            );

        Assert.StartsWith(
            "supporttoolkit-backup-review-",
            key
        );
    }

    [Fact]
    public void Create_MissingDestination_Throws()
    {
        var draft =
            new TicketDraft
            {
                Subject = "Example ticket",
                Body = "Example body."
            };

        Assert.Throws<ArgumentException>(
            () => TicketIdempotencyKeyFactory.Create(
                "backup-review",
                draft,
                " "
            )
        );
    }
}
