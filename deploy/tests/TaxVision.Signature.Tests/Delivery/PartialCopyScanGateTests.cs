using TaxVision.Signature.Application.Delivery;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Tests.Delivery;

/// <summary>Solo se espera un scan desconocido; un resultado final decide ya.</summary>
public sealed class PartialCopyScanGateTests
{
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Early = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Late = TimeSpan.FromMinutes(5);

    [Fact]
    public void An_available_copy_is_delivered() =>
        Assert.Equal(PartialCopyScanDecision.Deliver, PartialCopyScanGate.ForUploadedCopy(Available(), Early, MaxWait));

    [Theory]
    [InlineData(FileScanStatus.Rejected)]
    [InlineData(FileScanStatus.Infected)]
    public void A_copy_with_a_final_unavailable_outcome_fails_even_before_the_limit(FileScanStatus status) =>
        Assert.Equal(
            PartialCopyScanDecision.Fail,
            PartialCopyScanGate.ForUploadedCopy(Outcome(status), Early, MaxWait)
        );

    [Fact]
    public void An_unknown_copy_waits_within_the_limit() =>
        Assert.Equal(PartialCopyScanDecision.Wait, PartialCopyScanGate.ForUploadedCopy(null, Early, MaxWait));

    [Fact]
    public void An_unknown_copy_is_delivered_unscanned_after_the_limit() =>
        Assert.Equal(
            PartialCopyScanDecision.DeliverUnscanned,
            PartialCopyScanGate.ForUploadedCopy(null, Late, MaxWait)
        );

    [Fact]
    public void An_unknown_signature_image_is_waited_for_within_the_limit()
    {
        Assert.True(PartialCopyScanGate.ShouldWaitForImage(null, Early, MaxWait));
        Assert.False(PartialCopyScanGate.ShouldWaitForImage(null, Late, MaxWait));
    }

    [Theory]
    [InlineData(FileScanStatus.Rejected)]
    [InlineData(FileScanStatus.Infected)]
    public void A_signature_image_with_a_final_outcome_is_not_waited_for(FileScanStatus status) =>
        Assert.False(PartialCopyScanGate.ShouldWaitForImage(Outcome(status), Early, MaxWait));

    private static FileMetadataRef Available() =>
        FileMetadataRef.ForAvailable(Guid.NewGuid(), Guid.NewGuid(), "k", "application/pdf", 10, new string('a', 64));

    private static FileMetadataRef Outcome(FileScanStatus status) =>
        status == FileScanStatus.Infected
            ? FileMetadataRef.ForInfected(Guid.NewGuid(), Guid.NewGuid(), "virus")
            : FileMetadataRef.ForRejected(Guid.NewGuid(), Guid.NewGuid(), "EmptyFile");
}
