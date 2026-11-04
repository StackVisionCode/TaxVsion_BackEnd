using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests.Commands.ChangePreparer;
using TaxVision.Signature.Application.Requests.Preparer;
using TaxVision.Signature.Tests.Delivery;

namespace TaxVision.Signature.Tests.Application.Offboarding;

/// <summary>El admin cambia a mano el preparador de una solicitud enviada: se guarda, se avisa y se audita.</summary>
public sealed class ChangePreparerHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly Guid _newPreparer = Guid.NewGuid();
    private readonly InMemoryStaffWork _staffWork = new();
    private readonly InMemoryMembers _members = new();
    private readonly InMemoryProfiles _profiles = new();
    private readonly ChainAudit _audit = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly CapturingBus _bus = new();

    private Task<BuildingBlocks.Results.Result> Change(TaxVision.Signature.Domain.Requests.SignatureRequest request) =>
        ChangePreparerHandler.Handle(
            new ChangePreparerCommand(Tenant, request.Id, _newPreparer, _admin),
            new SingleRequestRepository(request),
            new PreparerReassigner(
                _staffWork,
                _members,
                _profiles,
                new ProfileBackedEffectiveSignature(_profiles),
                _audit,
                PreparerRequests.Codes
            ),
            _unitOfWork,
            new RecordingListCache(),
            _bus,
            new FixedCorrelation(),
            CancellationToken.None
        );

    [Fact]
    public async Task The_admin_change_is_saved_and_announced()
    {
        _members.Items.Add(PreparerRequests.Member(Tenant, _newPreparer));
        _staffWork.Identities[_newPreparer] = new PreparerIdentitySnapshot("P99887766", "John Roe, CPA", null);
        var request = PreparerRequests.Sent(Tenant, Guid.NewGuid(), Guid.NewGuid());

        var result = await Change(request);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(_newPreparer, request.Preparer!.UserId);
        Assert.Equal(1, _unitOfWork.Saves);
        var changed = Assert.Single(_bus.Messages.OfType<SignaturePreparerChangedIntegrationEvent>());
        Assert.Equal(_admin, changed.ChangedByUserId);
        Assert.Equal(SignaturePreparerChangeReasons.Manual, changed.Reason);
        Assert.Single(_audit.Appended);
    }

    [Fact]
    public async Task A_rejected_change_saves_and_announces_nothing()
    {
        var request = PreparerRequests.Sent(Tenant, Guid.NewGuid(), Guid.NewGuid());

        var result = await Change(request);

        Assert.Equal(PreparerReassigner.UserNotActive.Code, result.Error.Code);
        Assert.Equal(0, _unitOfWork.Saves);
        Assert.Empty(_bus.Messages);
    }

    [Fact]
    public async Task An_unknown_request_is_not_found()
    {
        var other = PreparerRequests.Sent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var result = await Change(other);

        Assert.Equal("Signature.Request.NotFound", result.Error.Code);
    }
}
