using System.Reflection;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Admin;
using TaxVision.Signature.Application.Requests.Commands.Cancel;
using TaxVision.Signature.Application.Requests.Commands.Send;
using TaxVision.Signature.Application.Requests.Commands.SignAsPreparer;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Tests.Application.Offboarding;
using TaxVision.Signature.Tests.Delivery;

namespace TaxVision.Signature.Tests.Application;

/// <summary>Work Profile: los eventos de la familia llevan el cliente de la solicitud y Sent lleva quién la envió.</summary>
public sealed class SignatureRequestLifecycleEventsTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Author = Guid.NewGuid();
    private static readonly Guid Preparer = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();

    [Fact]
    public async Task Sent_carries_the_sender_and_the_customer()
    {
        var request = Ready(Customer);
        var sender = Guid.NewGuid();
        var bus = new CapturingBus();

        var result = await SendSignatureRequestHandler.Handle(
            new SendSignatureRequestCommand(Tenant, request.Id, sender),
            new SingleRequestRepository(request),
            new FakeTokens(),
            new CountingUnitOfWork(),
            bus,
            new FixedCorrelation(),
            new RecordingListCache(),
            new UtcOffice(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var sent = Assert.Single(bus.Messages.OfType<SignatureRequestSentIntegrationEvent>());
        Assert.Equal(sender, sent.SentByUserId);
        Assert.Equal(Customer, sent.CustomerId);
    }

    [Fact]
    public async Task Sent_without_customer_signers_has_no_customer()
    {
        var request = Ready(customerId: null);
        var bus = new CapturingBus();

        await SendSignatureRequestHandler.Handle(
            new SendSignatureRequestCommand(Tenant, request.Id),
            new SingleRequestRepository(request),
            new FakeTokens(),
            new CountingUnitOfWork(),
            bus,
            new FixedCorrelation(),
            new RecordingListCache(),
            new UtcOffice(),
            CancellationToken.None
        );

        var sent = Assert.Single(bus.Messages.OfType<SignatureRequestSentIntegrationEvent>());
        Assert.Null(sent.SentByUserId);
        Assert.Null(sent.CustomerId);
    }

    [Fact]
    public async Task Canceled_carries_the_customer()
    {
        var request = InProgress(Customer);
        var bus = new CapturingBus();

        var result = await CancelSignatureRequestHandler.Handle(
            new CancelSignatureRequestCommand(Tenant, request.Id, Author, "Author", null),
            new SingleRequestRepository(request),
            new CountingUnitOfWork(),
            bus,
            new FixedCorrelation(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(
            Customer,
            Assert.Single(bus.Messages.OfType<SignatureRequestCanceledIntegrationEvent>()).CustomerId
        );
    }

    [Fact]
    public async Task PreparerSigned_carries_the_customer()
    {
        var request = InProgress(Customer);
        var bus = new CapturingBus();

        var result = await SignAsPreparerHandler.Handle(
            new SignAsPreparerCommand(Tenant, request.Id, Preparer, null, null),
            new SingleRequestRepository(request),
            new CountingUnitOfWork(),
            bus,
            new FixedCorrelation(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(Customer, Assert.Single(bus.Messages.OfType<PreparerSignedIntegrationEvent>()).CustomerId);
    }

    [Fact]
    public async Task Completed_carries_the_customer()
    {
        var request = InProgress(Customer);
        foreach (var signer in request.Signers)
            Assert.True(request.MarkSignerSigned(signer.Id, DateTime.UtcNow, "127.0.0.1", "tests").IsSuccess);
        // Deja la solicitud colgada para que la reposición publique Completed por el camino normal.
        SetPrivate(request, nameof(SignatureRequest.Status), SignatureRequestStatus.InProgress);
        SetPrivate(request, nameof(SignatureRequest.CompletedAtUtc), null);
        var bus = new CapturingBus();

        var result = await ReconcileCompletionHandler.Handle(
            new ReconcileCompletionCommand(Tenant, request.Id, false, false),
            new SingleRequestRepository(request),
            new CountingUnitOfWork(),
            bus,
            new FixedCorrelation(),
            new RecordingListCache(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(
            Customer,
            Assert.Single(bus.Messages.OfType<SignatureRequestCompletedIntegrationEvent>()).CustomerId
        );
    }

    // ----- helpers -----

    /// <summary>Borrador listo para enviar: un firmante externo primero y, si hay, uno del cliente después.</summary>
    private static SignatureRequest Ready(Guid? customerId)
    {
        var request = PreparerRequests.DraftWithPreparer(Tenant, Author, Preparer);
        var document = request.Documents.Single();
        AddSigner(request, document.Id, "external", null, 0);
        AddSigner(request, document.Id, "client", customerId, 1);
        request.AttachVerifiedDocument(document.Id, VerifiedDocument.Create(PreparerRequests.Hash('a'), 50).Value);
        return request;
    }

    private static SignatureRequest InProgress(Guid? customerId)
    {
        var request = Ready(customerId);
        Assert.True(request.Send(DateTime.UtcNow).IsSuccess);
        return request;
    }

    private static void AddSigner(SignatureRequest request, Guid documentId, string name, Guid? customerId, int index)
    {
        var signer = request
            .AddSigner(
                TestSignatureLimits.Default,
                SignerEmail.Create($"{name}@example.test").Value,
                SignerFullName.Create($"Signer {name}").Value,
                customerId
            )
            .Value;
        Assert.True(
            request
                .PlaceField(
                    TestSignatureLimits.MaxFieldsPerSigner,
                    signer.Id,
                    documentId,
                    SignatureFieldKind.Signature,
                    FieldPosition.Create(1, 0.1, 0.1 + index * 0.1, 0.2, 0.05).Value,
                    null,
                    true
                )
                .IsSuccess
        );
    }

    private static void SetPrivate(SignatureRequest request, string property, object? value) =>
        typeof(SignatureRequest)
            .GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(request, value);

    private sealed class FakeTokens : ISigningTokenService
    {
        public string Issue(SigningTokenPayload payload) => "token";

        public Result<SigningTokenPayload> Verify(string token) => throw new NotSupportedException();
    }

    private sealed class UtcOffice : IOfficeTimeZone
    {
        public Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult(ResolvedTimeZone.Utc);
    }
}
