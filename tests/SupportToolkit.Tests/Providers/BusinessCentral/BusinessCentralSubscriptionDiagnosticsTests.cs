using SupportToolkit.Providers.BusinessCentral.Subscriptions;
using SupportToolkit.Providers.BusinessCentral.Subscriptions.Dtos;

namespace SupportToolkit.Tests.Providers.BusinessCentral;

public class BusinessCentralSubscriptionDiagnosticsTests
{
    private static BusinessCentralSubscriptionData Sample()
    {
        return new BusinessCentralSubscriptionData(
            [
                Header("000000001", "CUST-SECRET-1", "Active", "_x0020_", "2026-10-31"),
                Header("000000002", "CUST-SECRET-2", "Suspended", "_x0020_", "0001-01-01"),
                Header("000000025", "CUST-SECRET-3", "Active", "All", "0001-01-01"),
                Header("000000025", "CUST-SECRET-4", "Aktiv", "Invoice", null),
                null
            ],
            [
                Line("000000001", 10000, "Item", "MON-ITEM", "Sandbox", "Secret Company Ltd"),
                Line("000000001", 20000, "Item", "MON-ITEM", "", ""),
                Line("000000002", 10000, "_x0020_", "MON-ITEM", "", ""),
                Line("000000777", 10000, "G_x002F_L_x0020_Account", "OTHER", "", ""),
                Line("000000777", 10000, "Item", "OTHER", "", "")
            ]);
    }

    [Fact]
    public void Output_ReportsDistinctValuesAndCounts()
    {
        var text = Render(Sample());

        Assert.Contains("Headers: 5 (1 empty records)", text);
        Assert.Contains("Lines:   5 (0 empty records)", text);
        Assert.Contains("'Active' x 2", text);
        Assert.Contains("'Suspended' x 1", text);
        Assert.Contains("'Aktiv' x 1", text);
        Assert.Contains("'All' x 1", text);
        Assert.Contains("'Invoice' x 1", text);
        Assert.Contains("(blank) x 2", text);
        Assert.Contains("'G/L Account' x 1", text);
        Assert.Contains("empty-date sentinel x 2", text);
        Assert.Contains("missing x 1", text);
    }

    [Fact]
    public void Output_ReportsJoinIntegrity()
    {
        var text = Render(Sample());

        Assert.Contains(
            "header numbers shared by more than one header: 1", text);
        Assert.Contains(
            "(subscriptionNumber, lineNumber) duplicated:    1", text);
        Assert.Contains(
            "lines without a matching header:                2", text);
        Assert.Contains(
            "headers without any line:                       2", text);
    }

    [Fact]
    public void Output_ReportsMonitoringItemAndContext()
    {
        var text = Render(Sample());

        Assert.Contains("lines carrying the item number: 3", text);
        Assert.Contains(
            "subscriptions with more than one such line: 1", text);
        Assert.Contains("environment populated: 1 of 5", text);
        Assert.Contains("company populated:     1 of 5", text);
        Assert.Contains("company conflicting:     1", text);
        Assert.Contains("environment conflicting: 0", text);
    }

    [Fact]
    public void Output_NeverContainsRowLevelValues()
    {
        var text = Render(Sample());

        Assert.DoesNotContain("CUST-SECRET", text);
        Assert.DoesNotContain("Secret Company", text);
        Assert.DoesNotContain("000000001", text);
        Assert.DoesNotContain("000000025", text);
        Assert.DoesNotContain("MON-ITEM", text);
    }

    [Fact]
    public void MissingItemNumber_IsStatedNotGuessed()
    {
        var text =
            Render(Sample(), itemNumber: null);

        Assert.Contains("not configured for this profile", text);
    }

    [Fact]
    public void ValueDisplay_IsBoundedAndSingleLine()
    {
        var hostile =
            "X\r\nINJECT" + new string('y', 200);

        var data =
            new BusinessCentralSubscriptionData(
                [Header("1", "c", hostile, "_x0020_", null)],
                []);

        var text = Render(data);

        Assert.DoesNotContain("INJECT" + new string('y', 100), text);
        Assert.DoesNotContain("X\r\nINJECT", text);
    }

    [Fact]
    public void EmptyData_ProducesAReport()
    {
        var text =
            Render(new BusinessCentralSubscriptionData([], []));

        Assert.Contains("Headers: 0", text);
        Assert.Contains("(none)", text);
    }

    private static string Render(
        BusinessCentralSubscriptionData data,
        string? itemNumber = "MON-ITEM")
    {
        using var writer = new StringWriter();

        BusinessCentralSubscriptionDiagnostics.Write(
            writer,
            data,
            itemNumber,
            "Sandbox",
            "Synthetic Company");

        return writer.ToString();
    }

    private static BusinessCentralSubscriptionHeaderDto Header(
        string number,
        string customer,
        string status,
        string blocked,
        string? endDate)
    {
        return new BusinessCentralSubscriptionHeaderDto
        {
            Number = number,
            SellToCustomerNumber = customer,
            Status = status,
            Blocked = blocked,
            Type = "Sales_x0020_Invoice",
            EndDate = endDate
        };
    }

    private static BusinessCentralSubscriptionLineDto Line(
        string subscription,
        int lineNumber,
        string type,
        string number,
        string environment,
        string company)
    {
        return new BusinessCentralSubscriptionLineDto
        {
            SubscriptionNumber = subscription,
            LineNumber = lineNumber,
            Type = type,
            Number = number,
            OmitLine = false,
            Environment = environment,
            Company = company
        };
    }
}
