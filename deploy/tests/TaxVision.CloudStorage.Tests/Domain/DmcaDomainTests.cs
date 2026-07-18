using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Legal;

namespace TaxVision.CloudStorage.Tests.Domain;

/// <summary>Fase L1.3 — DmcaNotice y los metodos de takedown de FileObject.</summary>
public sealed class DmcaDomainTests
{
    private static FileObject AvailableFile()
    {
        var tenantId = Guid.NewGuid();
        var key = ObjectKey.Create($"tenants/{tenantId:N}/tenant/documents/2025/{Guid.NewGuid():N}.pdf").Value;
        var file = FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2025,
                key,
                "return.pdf",
                "application/pdf",
                10,
                Guid.NewGuid(),
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(24)
            )
            .Value;
        file.MarkPendingScan();
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", DateTime.UtcNow);
        return file;
    }

    private static DmcaNotice RegisteredNotice(Guid fileId) =>
        DmcaNotice
            .Register(
                Guid.NewGuid(),
                Guid.NewGuid(),
                fileId,
                "Acme Rights Holder",
                ClaimantEmail.Create("legal@acme.example").Value,
                "A photograph of the Golden Gate Bridge",
                "The exact same photograph re-uploaded without permission",
                swornStatementAccepted: true,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;

    [Fact]
    public void ClaimantEmail_rejects_a_malformed_address()
    {
        var result = ClaimantEmail.Create("not-an-email");

        Assert.True(result.IsFailure);
        Assert.Equal(DmcaErrors.InvalidClaimantEmail, result.Error);
    }

    [Fact]
    public void Register_without_the_sworn_statement_fails()
    {
        var result = DmcaNotice.Register(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Acme Rights Holder",
            ClaimantEmail.Create("legal@acme.example").Value,
            "A photograph",
            "The same photograph",
            swornStatementAccepted: false,
            Guid.NewGuid(),
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal(DmcaErrors.SwornStatementRequired, result.Error);
    }

    [Fact]
    public void Register_with_all_required_fields_succeeds_as_Received()
    {
        var notice = RegisteredNotice(Guid.NewGuid());

        Assert.Equal(DmcaNoticeStatus.Received, notice.Status);
    }

    [Fact]
    public void SubmitCounterNotice_on_a_received_notice_succeeds()
    {
        var notice = RegisteredNotice(Guid.NewGuid());

        var result = notice.SubmitCounterNotice("This is my own original work.", Guid.NewGuid(), DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(DmcaNoticeStatus.CounterNoticeSubmitted, notice.Status);
    }

    [Fact]
    public void SubmitCounterNotice_with_empty_text_fails()
    {
        var notice = RegisteredNotice(Guid.NewGuid());

        var result = notice.SubmitCounterNotice("   ", Guid.NewGuid(), DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(DmcaErrors.CounterNoticeTextRequired, result.Error);
    }

    [Fact]
    public void SubmitCounterNotice_twice_fails()
    {
        var notice = RegisteredNotice(Guid.NewGuid());
        notice.SubmitCounterNotice("Dispute #1", Guid.NewGuid(), DateTime.UtcNow);

        var result = notice.SubmitCounterNotice("Dispute #2", Guid.NewGuid(), DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(DmcaErrors.InvalidTransition, result.Error);
    }

    [Fact]
    public void Reinstate_directly_from_Received_succeeds()
    {
        var notice = RegisteredNotice(Guid.NewGuid());

        var result = notice.Reinstate(Guid.NewGuid(), "Claim withdrawn", DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(DmcaNoticeStatus.Reinstated, notice.Status);
    }

    [Fact]
    public void Reinstate_after_a_counter_notice_succeeds()
    {
        var notice = RegisteredNotice(Guid.NewGuid());
        notice.SubmitCounterNotice("This is my own original work.", Guid.NewGuid(), DateTime.UtcNow);

        var result = notice.Reinstate(Guid.NewGuid(), "Counter-notice accepted", DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(DmcaNoticeStatus.Reinstated, notice.Status);
    }

    [Fact]
    public void Reinstate_twice_fails()
    {
        var notice = RegisteredNotice(Guid.NewGuid());
        notice.Reinstate(Guid.NewGuid(), null, DateTime.UtcNow);

        var result = notice.Reinstate(Guid.NewGuid(), null, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(DmcaErrors.InvalidTransition, result.Error);
    }

    [Fact]
    public void BlockForTakedown_on_an_Available_file_succeeds()
    {
        var file = AvailableFile();

        var result = file.BlockForTakedown(DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(FileStatus.BlockedByPolicy, file.Status);
    }

    [Fact]
    public void BlockForTakedown_on_a_file_that_is_not_Available_fails()
    {
        var file = AvailableFile();
        file.SoftDelete(DateTime.UtcNow, TimeSpan.FromDays(30));

        var result = file.BlockForTakedown(DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.InvalidTransition, result.Error);
    }

    [Fact]
    public void ReinstateFromTakedown_on_a_blocked_file_succeeds()
    {
        var file = AvailableFile();
        file.BlockForTakedown(DateTime.UtcNow);

        var result = file.ReinstateFromTakedown(DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(FileStatus.Available, file.Status);
    }

    [Fact]
    public void ReinstateFromTakedown_on_a_file_that_is_not_blocked_fails()
    {
        var file = AvailableFile();

        var result = file.ReinstateFromTakedown(DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.InvalidTransition, result.Error);
    }
}
