using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests.Preparer;
using TaxVision.Signature.Domain.Audit;

namespace TaxVision.Signature.Tests.Application.Offboarding;

/// <summary>
/// El cambio de preparador exige un empleado activo con su propia identidad 8879, conserva la firma de
/// oficina, sustituye la personal del anterior y se audita antes de tocar la solicitud.
/// </summary>
public sealed class PreparerReassignerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private readonly Guid _leaver = Guid.NewGuid();
    private readonly Guid _successor = Guid.NewGuid();
    private readonly InMemoryStaffWork _staffWork = new();
    private readonly InMemoryMembers _members = new();
    private readonly InMemoryProfiles _profiles = new();
    private readonly ChainAudit _audit = new();

    private PreparerReassigner Reassigner() =>
        new(
            _staffWork,
            _members,
            _profiles,
            new ProfileBackedEffectiveSignature(_profiles),
            _audit,
            PreparerRequests.Codes
        );

    private void SuccessorIsReady(params string[] codes)
    {
        _members.Items.Add(PreparerRequests.Member(Tenant, _successor, true, codes));
        _staffWork.Identities[_successor] = new PreparerIdentitySnapshot("P99887766", "John Roe, CPA", "CPA");
    }

    [Fact]
    public async Task The_successor_takes_over_with_his_own_identity_and_the_change_is_audited()
    {
        SuccessorIsReady();
        var request = PreparerRequests.Sent(Tenant, Guid.NewGuid(), _leaver);

        var result = await Reassigner().ReassignAsync(request, _successor, null, "Offboarding", CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.True(result.Value.Changed);
        Assert.Equal(_leaver, result.Value.PreviousPreparerUserId);
        Assert.Equal(_successor, request.Preparer!.UserId);
        Assert.Equal("P99887766", request.Preparer.PtinOrEfin);
        var entry = Assert.Single(_audit.Appended);
        Assert.Equal(SignatureAuditEventKind.PreparerChanged, entry.Kind);
        Assert.Contains("P12345678", entry.PayloadJson);
        Assert.Contains("P99887766", entry.PayloadJson);
    }

    [Fact]
    public async Task Without_an_identity_on_file_nothing_changes()
    {
        _members.Items.Add(PreparerRequests.Member(Tenant, _successor));
        var request = PreparerRequests.Sent(Tenant, Guid.NewGuid(), _leaver);

        var result = await Reassigner().ReassignAsync(request, _successor, null, "Offboarding", CancellationToken.None);

        Assert.Equal(PreparerReassigner.NoIdentityOnFile.Code, result.Error.Code);
        Assert.Equal(_leaver, request.Preparer!.UserId);
        Assert.Empty(_audit.Appended);
    }

    [Fact]
    public async Task An_inactive_or_unknown_user_cannot_be_the_preparer()
    {
        _staffWork.Identities[_successor] = new PreparerIdentitySnapshot("P99887766", "John Roe, CPA", null);
        var request = PreparerRequests.Sent(Tenant, Guid.NewGuid(), _leaver);

        var unknown = await Reassigner()
            .ReassignAsync(request, _successor, null, "Offboarding", CancellationToken.None);
        _members.Items.Add(PreparerRequests.Member(Tenant, _successor, active: false));
        var inactive = await Reassigner()
            .ReassignAsync(request, _successor, null, "Offboarding", CancellationToken.None);

        Assert.Equal(PreparerReassigner.UserNotActive.Code, unknown.Error.Code);
        Assert.Equal(PreparerReassigner.UserNotActive.Code, inactive.Error.Code);
        Assert.Equal(_leaver, request.Preparer!.UserId);
    }

    [Fact]
    public async Task If_the_audit_trail_fails_the_request_is_left_untouched()
    {
        SuccessorIsReady();
        _audit.Fail = true;
        var request = PreparerRequests.Sent(Tenant, Guid.NewGuid(), _leaver);

        var result = await Reassigner().ReassignAsync(request, _successor, null, "Offboarding", CancellationToken.None);

        Assert.Equal(PreparerReassigner.AuditUnavailable.Code, result.Error.Code);
        Assert.Equal(_leaver, request.Preparer!.UserId);
    }

    [Fact]
    public async Task The_office_signature_is_kept()
    {
        SuccessorIsReady("signature.sign_own");
        var office = _profiles.Add(Tenant, ownerUserId: null);
        _profiles.Add(Tenant, _successor);
        var request = PreparerRequests.Sent(Tenant, Guid.NewGuid(), _leaver, office.FileId);

        await Reassigner().ReassignAsync(request, _successor, null, "Offboarding", CancellationToken.None);

        Assert.Equal(office.FileId, request.PreparerSignatureFileId);
    }

    [Fact]
    public async Task The_leaver_personal_signature_becomes_the_successor_one()
    {
        SuccessorIsReady("signature.sign_own");
        var leaverSignature = _profiles.Add(Tenant, _leaver);
        var successorSignature = _profiles.Add(Tenant, _successor);
        var request = PreparerRequests.Sent(Tenant, Guid.NewGuid(), _leaver, leaverSignature.FileId);

        await Reassigner().ReassignAsync(request, _successor, null, "Offboarding", CancellationToken.None);

        Assert.Equal(successorSignature.FileId, request.PreparerSignatureFileId);
    }

    [Fact]
    public async Task Without_sign_own_the_personal_signature_falls_back_to_the_office_one_or_none()
    {
        SuccessorIsReady();
        var leaverSignature = _profiles.Add(Tenant, _leaver);
        _profiles.Add(Tenant, _successor);
        var request = PreparerRequests.Sent(Tenant, Guid.NewGuid(), _leaver, leaverSignature.FileId);

        await Reassigner().ReassignAsync(request, _successor, null, "Offboarding", CancellationToken.None);

        // Sin firma de oficina: sello tipográfico con el nombre del nuevo, nunca la imagen del anterior.
        Assert.Null(request.PreparerSignatureFileId);
    }

    [Fact]
    public async Task The_same_preparer_is_not_audited_again()
    {
        SuccessorIsReady();
        var request = PreparerRequests.Sent(Tenant, Guid.NewGuid(), _successor);

        var result = await Reassigner().ReassignAsync(request, _successor, null, "Manual", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Changed);
        Assert.Empty(_audit.Appended);
    }
}
