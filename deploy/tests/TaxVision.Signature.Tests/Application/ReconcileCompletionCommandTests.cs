using System.Reflection;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using TaxVision.Signature.Application.Admin;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Tests.Application.Offboarding;
using TaxVision.Signature.Tests.Delivery;

namespace TaxVision.Signature.Tests.Application;

/// <summary>SIG-1: la reposición completa lo que se quedó colgado y publica lo mismo que el camino normal, una vez.</summary>
public sealed class ReconcileCompletionCommandTests
{
    [Fact]
    public async Task AllSignedInProgress_CompletesAndPublishesOnce()
    {
        var request = StrandedRequest();
        var bus = new CapturingBus();
        var unitOfWork = new CountingUnitOfWork();

        var result = await HandleAsync(request, bus, unitOfWork, allowExpired: false, dryRun: false);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Completed);
        Assert.Equal(SignatureRequestStatus.Completed, request.Status);
        Assert.Equal(1, unitOfWork.Saves);
        Assert.Single(bus.Messages.OfType<SignatureRequestCompletedIntegrationEvent>());
        var ready = Assert.Single(bus.Messages.OfType<SignatureDocumentsReadyForSealingIntegrationEvent>());
        Assert.Equal(request.Documents.Select(d => d.Id), ready.DocumentIds);
    }

    [Fact]
    public async Task AlreadyCompleted_IsNoOp()
    {
        var request = StrandedRequest();
        var bus = new CapturingBus();
        await HandleAsync(request, bus, new CountingUnitOfWork(), allowExpired: false, dryRun: false);
        bus.Messages.Clear();
        var unitOfWork = new CountingUnitOfWork();

        var again = await HandleAsync(request, bus, unitOfWork, allowExpired: false, dryRun: false);

        Assert.True(again.IsSuccess);
        Assert.False(again.Value.Completed);
        Assert.Empty(bus.Messages);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task Expired_WithoutAllowExpired_IsRejected()
    {
        var request = StrandedRequest(status: SignatureRequestStatus.Expired);

        var result = await HandleAsync(request, new CapturingBus(), new CountingUnitOfWork(), false, false);

        Assert.True(result.IsFailure);
        Assert.Equal(SignatureRequestStatus.Expired, request.Status);
    }

    [Fact]
    public async Task Expired_WithAllowExpired_Completes()
    {
        var request = StrandedRequest(status: SignatureRequestStatus.Expired);
        var bus = new CapturingBus();

        var result = await HandleAsync(request, bus, new CountingUnitOfWork(), allowExpired: true, dryRun: false);

        Assert.True(result.Value.Completed);
        Assert.Equal(SignatureRequestStatus.Completed, request.Status);
        Assert.Single(bus.Messages.OfType<SignatureRequestCompletedIntegrationEvent>());
    }

    [Fact]
    public async Task DryRun_DoesNotTouchTheRequest()
    {
        var request = StrandedRequest();
        var bus = new CapturingBus();
        var unitOfWork = new CountingUnitOfWork();

        var result = await HandleAsync(request, bus, unitOfWork, allowExpired: false, dryRun: true);

        Assert.True(result.Value.Completed);
        Assert.Equal(SignatureRequestStatus.InProgress, request.Status);
        Assert.Empty(bus.Messages);
        Assert.Equal(0, unitOfWork.Saves);
    }

    private static Task<BuildingBlocks.Results.Result<ReconcileCompletionReport>> HandleAsync(
        SignatureRequest request,
        CapturingBus bus,
        CountingUnitOfWork unitOfWork,
        bool allowExpired,
        bool dryRun
    ) =>
        ReconcileCompletionHandler.Handle(
            new ReconcileCompletionCommand(request.TenantId, request.Id, allowExpired, dryRun),
            new SingleRequestRepository(request),
            unitOfWork,
            bus,
            new FixedCorrelation(),
            new RecordingListCache(),
            CancellationToken.None
        );

    /// <summary>
    /// Dos firmas que se pisaron (antes de SIG-1): los dos firmantes Signed y la solicitud sin completar. Se
    /// arma con firmas reales y se devuelve el estado al que dejaba la carrera.
    /// </summary>
    private static SignatureRequest StrandedRequest(SignatureRequestStatus status = SignatureRequestStatus.InProgress)
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(limits, Guid.NewGuid(), Guid.NewGuid(), "Stranded", null, "Fiscal", 72, false, false, false)
            .Value;
        var document = request.AddDocument(limits, Guid.NewGuid(), "Return").Value;
        var signers = new[] { "a", "b" }
            .Select(name =>
                request
                    .AddSigner(
                        limits,
                        SignerEmail.Create($"{name}@example.test").Value,
                        SignerFullName.Create($"Signer {name.ToUpperInvariant()}").Value,
                        null
                    )
                    .Value
            )
            .ToList();
        foreach (var signer in signers)
            Assert.True(
                request
                    .PlaceField(
                        TestSignatureLimits.MaxFieldsPerSigner,
                        signer.Id,
                        document.Id,
                        SignatureFieldKind.Signature,
                        FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value,
                        null,
                        true
                    )
                    .IsSuccess
            );
        Assert.True(
            request
                .AttachVerifiedDocument(
                    document.Id,
                    VerifiedDocument.Create(DocumentHash.Create(new string('a', 64)).Value, 5).Value
                )
                .IsSuccess
        );
        Assert.True(request.Send(DateTime.UtcNow).IsSuccess);
        foreach (var signer in signers)
            Assert.True(request.MarkSignerSigned(signer.Id, DateTime.UtcNow, "127.0.0.1", "tests").IsSuccess);

        // Estado que dejaba la carrera: la segunda escritura pisó el Completed de la primera.
        SetPrivate(request, nameof(SignatureRequest.Status), status);
        SetPrivate(request, nameof(SignatureRequest.CompletedAtUtc), null);
        return request;
    }

    private static void SetPrivate(SignatureRequest request, string property, object? value) =>
        typeof(SignatureRequest)
            .GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(request, value);
}
