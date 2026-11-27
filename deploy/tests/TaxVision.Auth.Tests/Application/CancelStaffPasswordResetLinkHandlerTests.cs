using BuildingBlocks.Results;
using TaxVision.Auth.Application.Credentials.Administration;
using TaxVision.Auth.Domain.Credentials;
using Xunit;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Cancelar el link pendiente (03 §3 fila 1b): sin correo al afectado (Q-04) y con auditoría.</summary>
public sealed class CancelStaffPasswordResetLinkHandlerTests
{
    private readonly OfficeCredentialFixture _office = new();

    private Task<Result> CancelAsync() =>
        CancelStaffPasswordResetLinkHandler.Handle(
            new CancelStaffPasswordResetLinkCommand(_office.Tenant.Id, _office.Owner.Id, _office.Employee.Id),
            _office.Users,
            _office.Credentials,
            new NoUserLoginLock(),
            _office.Options,
            _office.Audit,
            _office.Request,
            _office.Correlation,
            _office.UnitOfWork,
            CancellationToken.None
        );

    [Fact]
    public async Task Cancelling_revokes_the_pending_link_without_email()
    {
        var link = _office.SeedLink(_office.Employee, ResetOrigin.Administrative, _office.Owner.Id);

        var result = await CancelAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(PasswordResetTokenStatus.Cancelled, link.StatusAt(DateTime.UtcNow));
        Assert.Equal(ResetRevocationReason.CancelledByOffice, link.RevocationReason);
        Assert.Equal(_office.Owner.Id, link.RevokedByUserId);
        var log = Assert.Single(_office.Audit.Logs);
        Assert.Equal("auth.password.reset_cancelled_by_office", log.Action);
        Assert.Equal(_office.Employee.Id, log.TargetId);
        Assert.Empty(_office.Bus.Published);
    }

    [Fact]
    public async Task Without_a_pending_link_it_answers_no_pending_link() =>
        Assert.Equal("Auth.CredentialAdmin.NoPendingLink", (await CancelAsync()).Error.Code);

    [Fact]
    public async Task A_used_link_answers_already_used()
    {
        _office.SeedLink(_office.Employee, ResetOrigin.Administrative, _office.Owner.Id).MarkUsed();

        Assert.Equal("Auth.CredentialAdmin.LinkAlreadyUsed", (await CancelAsync()).Error.Code);
    }
}
