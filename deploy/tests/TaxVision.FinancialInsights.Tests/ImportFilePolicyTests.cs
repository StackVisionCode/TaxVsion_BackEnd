using TaxVision.FinancialInsights.Application.Imports;
using Xunit;

namespace TaxVision.FinancialInsights.Tests;

public sealed class ImportFilePolicyTests
{
    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("text/csv")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public void Accepts_supported_scanned_customer_documents(string type)
    {
        var customer = Guid.NewGuid();
        Assert.True(ImportFilePolicy.CanRegister("Customer", customer, customer, "Available", type, 100));
    }

    [Fact]
    public void Rejects_other_customer_pending_scan_and_unrecognized_content()
    {
        var customer = Guid.NewGuid();
        Assert.False(ImportFilePolicy.CanRegister("Customer", Guid.NewGuid(), customer, "Available", "application/pdf", 100));
        Assert.False(ImportFilePolicy.CanRegister("Customer", customer, customer, "PendingScan", "application/pdf", 100));
        Assert.False(ImportFilePolicy.CanRegister("Customer", customer, customer, "Available", "application/octet-stream", 100));
        Assert.False(ImportFilePolicy.CanRegister("Customer", customer, customer, "Available", "application/pdf", 26 * 1024 * 1024));
    }

    [Fact]
    public void Portal_can_register_own_pending_scan_but_not_unfinished_or_untrusted_files()
    {
        var customer = Guid.NewGuid();
        Assert.True(ImportFilePolicy.CanAwaitScan("Customer", customer, customer, "PendingScan", "application/pdf", 100));
        Assert.True(ImportFilePolicy.CanAwaitScan("Customer", customer, customer, "Scanning", "text/csv", 100));
        Assert.False(ImportFilePolicy.CanAwaitScan("Customer", customer, customer, "PendingUpload", "application/pdf", 100));
        Assert.False(ImportFilePolicy.CanAwaitScan("Customer", Guid.NewGuid(), customer, "PendingScan", "application/pdf", 100));
        Assert.False(ImportFilePolicy.CanAwaitScan("Customer", customer, customer, "PendingScan", "application/octet-stream", 100));
    }
}
