using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests.Commands.ResendSignerInvitation;
using TaxVision.Signature.Application.Requests.Commands.Send;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Tests.Application.Offboarding;
using TaxVision.Signature.Tests.Delivery;

namespace TaxVision.Signature.Tests.Application;

/// <summary>Firma con orden: el envío y el reenvío solo alcanzan al firmante del turno.</summary>
public sealed class SequentialInvitationTests
{
    [Fact]
    public async Task Sending_in_order_invites_only_the_first_signer()
    {
        var (request, signers) = Ready(sequential: true);
        var bus = new CapturingBus();

        var result = await Send(request, bus);

        Assert.True(result.IsSuccess);
        var invited = Assert.Single(bus.Messages.OfType<SignerInvitedIntegrationEvent>());
        Assert.Equal(signers[0].Id, invited.SignerId);
        Assert.Null(signers[1].CurrentTokenId);
        Assert.Null(signers[2].CurrentTokenId);
    }

    [Fact]
    public async Task Sending_without_order_invites_everyone()
    {
        var (request, _) = Ready(sequential: false);
        var bus = new CapturingBus();

        await Send(request, bus);

        Assert.Equal(3, bus.Messages.OfType<SignerInvitedIntegrationEvent>().Count());
    }

    [Fact]
    public async Task Resending_to_a_signer_whose_turn_has_not_come_fails()
    {
        var (request, signers) = Ready(sequential: true);
        await Send(request, new CapturingBus());
        var bus = new CapturingBus();

        var result = await Resend(request, signers[1].Id, bus);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.NotYourTurn", result.Error.Code);
        Assert.Empty(bus.Messages.OfType<SignerInvitedIntegrationEvent>());
        Assert.Null(signers[1].CurrentTokenId);
    }

    [Fact]
    public async Task Resending_to_the_current_signer_works()
    {
        var (request, signers) = Ready(sequential: true);
        await Send(request, new CapturingBus());
        var bus = new CapturingBus();

        var result = await Resend(request, signers[0].Id, bus);

        Assert.True(result.IsSuccess);
        Assert.Equal(signers[0].Id, Assert.Single(bus.Messages.OfType<SignerInvitedIntegrationEvent>()).SignerId);
    }

    private static Task<Result> Send(SignatureRequest request, CapturingBus bus) =>
        SendSignatureRequestHandler.Handle(
            new SendSignatureRequestCommand(request.TenantId, request.Id, Guid.NewGuid()),
            new SingleRequestRepository(request),
            new FakeTokens(),
            new CountingUnitOfWork(),
            bus,
            new FixedCorrelation(),
            new RecordingListCache(),
            new UtcOffice(),
            CancellationToken.None
        );

    private static Task<Result> Resend(SignatureRequest request, Guid signerId, CapturingBus bus) =>
        ResendSignerInvitationHandler.Handle(
            new ResendSignerInvitationCommand(request.TenantId, request.Id, signerId),
            new SingleRequestRepository(request),
            new FakeTokens(),
            new NoDenylist(),
            new CountingUnitOfWork(),
            bus,
            new FixedCorrelation(),
            CancellationToken.None
        );

    private static (SignatureRequest Request, IReadOnlyList<Signer> Signers) Ready(bool sequential)
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(
                limits,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Engagement",
                null,
                "Fiscal",
                72,
                sequential,
                false,
                false
            )
            .Value;
        var document = request.AddDocument(limits, Guid.NewGuid(), "Engagement").Value;
        var signers = new List<Signer>();
        for (var index = 1; index <= 3; index++)
        {
            var signer = request
                .AddSigner(
                    limits,
                    SignerEmail.Create($"signer{index}@example.test").Value,
                    SignerFullName.Create($"Signer {index}").Value,
                    null
                )
                .Value;
            Assert.True(
                request
                    .PlaceField(
                        TestSignatureLimits.MaxFieldsPerSigner,
                        signer.Id,
                        document.Id,
                        SignatureFieldKind.Signature,
                        FieldPosition.Create(1, 0.1, 0.1 * index, 0.2, 0.05).Value,
                        null,
                        true
                    )
                    .IsSuccess
            );
            signers.Add(signer);
        }
        request.AttachVerifiedDocument(document.Id, VerifiedDocument.Create(PreparerRequests.Hash('a'), 1).Value);
        return (request, signers);
    }

    private sealed class FakeTokens : ISigningTokenService
    {
        public string Issue(SigningTokenPayload payload) => "token";

        public Result<SigningTokenPayload> Verify(string token) => throw new NotSupportedException();
    }

    private sealed class NoDenylist : IJtiDenylist
    {
        public Task<bool> IsRevokedAsync(string jti, CancellationToken ct = default) => Task.FromResult(false);

        public Task RevokeAsync(string jti, DateTime expiresAtUtc, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class UtcOffice : IOfficeTimeZone
    {
        public Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult(ResolvedTimeZone.Utc);
    }
}
