using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Application.Delivery;
using TaxVision.Signature.Domain.Audit;
using TaxVision.Signature.Domain.Delivery;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Tests.Delivery;

/// <summary>
/// Entrega del documento firmado sin shares: un correo por firmante con todos sus archivos, enlace propio con
/// vigencia de la oficina, idempotente, y una página pública que vence, se reemite al mismo email y se revoca.
/// </summary>
public sealed class SignedDocumentDeliveryTests
{
    private static readonly DeliveryItemSpec AnyItem = new(Guid.NewGuid(), DeliveryItemKind.SignedDocument, null, "Return", 10);

    // ------------------------------- Dominio -------------------------------

    [Fact]
    public void A_delivery_expires_at_the_office_lifetime_and_a_reissue_rotates_the_secret()
    {
        var now = DateTime.UtcNow;
        var first = DeliveryToken.Generate();
        var delivery = Issue(first, TimeSpan.FromDays(30), now);

        Assert.Equal(DeliveryStatus.Active, delivery.StatusAt(now.AddDays(29)));
        Assert.Equal(DeliveryStatus.Expired, delivery.StatusAt(now.AddDays(30)));

        var second = DeliveryToken.Generate();
        Assert.True(delivery.Reissue(second, TimeSpan.FromDays(30), now.AddDays(31)).IsSuccess);

        Assert.Equal(second.Hash, delivery.TokenHash);
        Assert.NotEqual(first.Hash, delivery.TokenHash);
        Assert.Equal(2, delivery.IssueCount);
        Assert.Equal(DeliveryStatus.Active, delivery.StatusAt(now.AddDays(32)));
    }

    [Fact]
    public void A_revoked_delivery_cannot_be_opened_nor_reissued()
    {
        var now = DateTime.UtcNow;
        var delivery = Issue(DeliveryToken.Generate(), TimeSpan.FromDays(30), now);

        delivery.Revoke(Guid.NewGuid(), now);

        Assert.Equal(DeliveryStatus.Revoked, delivery.StatusAt(now));
        Assert.True(delivery.RegisterAccess(now).IsFailure);
        Assert.True(delivery.Reissue(DeliveryToken.Generate(), TimeSpan.FromDays(1), now).IsFailure);
    }

    [Fact]
    public void The_recipient_can_ask_for_a_new_link_only_after_the_cooldown()
    {
        var now = DateTime.UtcNow;
        var delivery = Issue(DeliveryToken.Generate(), TimeSpan.FromDays(1), now);

        Assert.False(delivery.CanBeReissuedPubliclyAt(now.AddMinutes(1)));
        Assert.True(delivery.CanBeReissuedPubliclyAt(now.Add(SignedDocumentDelivery.PublicReissueCooldown)));
    }

    // ------------------------------- Emisión -------------------------------

    [Fact]
    public async Task Each_signer_gets_one_delivery_with_every_signed_document()
    {
        var kit = new DeliveryTestKit().Seal();

        var issued = await kit.Issuer().PrepareCompletedAsync(kit.Request, CancellationToken.None);

        Assert.Equal(2, issued.Count);
        Assert.All(issued, delivery => Assert.Equal(2, delivery.Delivery.Items.Count));
        Assert.All(issued, delivery => Assert.All(delivery.Delivery.Items, item => Assert.Equal(DeliveryItemKind.SignedDocument, item.Kind)));
        Assert.Equal(2, kit.Audit.Kinds.Count(kind => kind == SignatureAuditEventKind.DeliveryIssued));
    }

    [Fact]
    public async Task A_per_document_certificate_goes_only_to_who_signed_that_document()
    {
        var kit = new DeliveryTestKit(sendSealed: false, sendCertificate: true).Seal();

        var issued = await kit.Issuer().PrepareCompletedAsync(kit.Request, CancellationToken.None);

        var first = Assert.Single(issued, delivery => delivery.Delivery.SignerId == kit.FirstSigner.Id).Delivery;
        var item = Assert.Single(first.Items);
        Assert.Equal(DeliveryItemKind.Certificate, item.Kind);
        Assert.Equal(kit.Request.Documents[0].Id, item.DocumentId);
    }

    [Fact]
    public async Task Issuing_twice_does_not_duplicate_a_signers_delivery()
    {
        var kit = new DeliveryTestKit().Seal();
        var issuer = kit.Issuer();

        await issuer.PrepareCompletedAsync(kit.Request, CancellationToken.None);
        var again = await issuer.PrepareCompletedAsync(kit.Request, CancellationToken.None);

        Assert.Empty(again);
        Assert.Equal(2, kit.Deliveries.All.Count);
    }

    [Fact]
    public async Task The_event_carries_the_encrypted_token_and_the_office_lifetime()
    {
        var policy = DeliveryPolicy.Create(10, allowPublicReissue: false, attachDocuments: false, DeliveryPolicy.DefaultMaxAttachmentBytes).Value;
        var kit = new DeliveryTestKit(policy: policy).Seal();
        var issuer = kit.Issuer();
        var issued = await issuer.PrepareCompletedAsync(kit.Request, CancellationToken.None);

        await issuer.PublishAsync(issued[0], kit.Request, CancellationToken.None);

        var evt = Assert.IsType<SignedDocumentsDeliveryReadyIntegrationEvent>(Assert.Single(kit.Bus.Messages));
        Assert.Equal("Completed", evt.Purpose);
        Assert.DoesNotContain(issued[0].Token.Plain, evt.ProtectedToken.Schema);
        var token = kit.Codec.Unprotect<SignatureDeliveryLinkPayload>(
            evt.ProtectedToken,
            SignatureDeliveryPayloadSchemas.DeliveryLink,
            SignatureDeliveryPayloadSchemas.BindingFor(kit.Request.TenantId, evt.DeliveryId)
        );
        Assert.Equal(issued[0].Token.Plain, token.Token);
        Assert.Equal(DeliveryToken.HashOf(token.Token), issued[0].Delivery.TokenHash);
        Assert.False(evt.CanRequestNewLink);
        Assert.InRange((evt.ExpiresAtUtc - DateTime.UtcNow).TotalDays, 9.9, 10.1);
        Assert.Empty(evt.AttachmentFileIds);
    }

    [Theory]
    [InlineData(1024, 1)]
    [InlineData(2L * 1024 * 1024, 0)]
    public async Task Files_are_attached_only_when_the_office_wants_it_and_they_fit(long sizeBytes, int expectedPerSigner)
    {
        var policy = DeliveryPolicy.Create(30, true, attachDocuments: true, maxAttachmentBytes: 3L * 1024 * 1024).Value;
        var kit = new DeliveryTestKit(policy: policy).Seal(sizeBytes);
        var issuer = kit.Issuer();
        var issued = await issuer.PrepareCompletedAsync(kit.Request, CancellationToken.None);

        await issuer.PublishAsync(issued[0], kit.Request, CancellationToken.None);

        var evt = Assert.IsType<SignedDocumentsDeliveryReadyIntegrationEvent>(Assert.Single(kit.Bus.Messages));
        Assert.Equal(expectedPerSigner * 2, evt.AttachmentFileIds.Count);
    }

    [Fact]
    public async Task A_single_customer_signer_is_told_the_files_are_also_in_their_portal()
    {
        var kit = new DeliveryTestKit(firstSignerCustomerId: Guid.NewGuid()).Seal();
        var issuer = kit.Issuer();
        var issued = await issuer.PrepareCompletedAsync(kit.Request, CancellationToken.None);

        foreach (var delivery in issued)
            await issuer.PublishAsync(delivery, kit.Request, CancellationToken.None);

        var events = kit.Bus.Messages.OfType<SignedDocumentsDeliveryReadyIntegrationEvent>().ToList();
        Assert.True(events.Single(evt => evt.Signer.SignerId == kit.FirstSigner.Id).AvailableInClientPortal);
        Assert.False(events.Single(evt => evt.Signer.SignerId == kit.SecondSigner.Id).AvailableInClientPortal);
    }

    // ------------------------------- Coordinador -------------------------------

    [Fact]
    public async Task Nothing_is_sent_until_every_certificate_is_available_too()
    {
        var kit = new DeliveryTestKit(sendCertificate: true).Seal(certificatesAvailable: false);

        await HandleAvailable(kit, kit.Request.Documents[0].SealedFileId!.Value);

        Assert.Empty(kit.Deliveries.All);
        Assert.Empty(kit.Bus.Messages);
    }

    [Fact]
    public async Task When_the_last_file_is_available_one_email_per_signer_goes_out()
    {
        var kit = new DeliveryTestKit(sendCertificate: true).Seal();

        await HandleAvailable(kit, kit.Request.Documents[1].CertificateFileId!.Value);

        var events = kit.Bus.Messages.OfType<SignedDocumentsDeliveryReadyIntegrationEvent>().ToList();
        Assert.Equal(2, events.Count);
        Assert.All(events, evt => Assert.Equal(3, evt.Items.Count)); // 2 firmados + su certificado
    }

    [Fact]
    public async Task A_request_that_delivers_nothing_sends_nothing()
    {
        var kit = new DeliveryTestKit(sendSealed: false, sendCertificate: false).Seal();

        await HandleAvailable(kit, kit.Request.Documents[0].SealedFileId!.Value);

        Assert.Empty(kit.Bus.Messages);
    }

    [Fact]
    public async Task A_file_missing_from_the_projection_is_checked_against_cloud_storage()
    {
        var kit = new DeliveryTestKit(sendCertificate: true).Seal(certificatesAvailable: false);
        foreach (var document in kit.Request.Documents)
            kit.Storage.RemoteStatuses[document.CertificateFileId!.Value] = "Available";

        await HandleAvailable(kit, kit.Request.Documents[0].SealedFileId!.Value);

        Assert.Equal(2, kit.Bus.Messages.OfType<SignedDocumentsDeliveryReadyIntegrationEvent>().Count());
    }

    // ------------------------------- Recuperación desde la oficina -------------------------------

    [Fact]
    public async Task The_office_can_issue_deliveries_that_were_never_sent()
    {
        var kit = new DeliveryTestKit(sendCertificate: true).Seal(certificatesAvailable: false);
        foreach (var document in kit.Request.Documents)
            kit.Storage.RemoteStatuses[document.CertificateFileId!.Value] = "Available";

        var result = await Issue(kit);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal(2, kit.Bus.Messages.OfType<SignedDocumentsDeliveryReadyIntegrationEvent>().Count());
    }

    [Fact]
    public async Task Issuing_again_from_the_office_does_not_duplicate_deliveries()
    {
        var kit = new DeliveryTestKit().Seal();

        await Issue(kit);
        var again = await Issue(kit);

        Assert.True(again.IsSuccess);
        Assert.Equal(2, kit.Deliveries.All.Count);
        Assert.Equal(2, kit.Bus.Messages.OfType<SignedDocumentsDeliveryReadyIntegrationEvent>().Count());
    }

    [Fact]
    public async Task The_office_cannot_issue_while_the_files_are_still_processing()
    {
        var kit = new DeliveryTestKit(sendCertificate: true).Seal(certificatesAvailable: false);

        var result = await Issue(kit);

        Assert.Equal("Signature.Delivery.FilesNotReady", result.Error.Code);
        Assert.Empty(kit.Deliveries.All);
    }

    [Fact]
    public async Task The_office_cannot_issue_for_a_request_that_delivers_nothing()
    {
        var kit = new DeliveryTestKit(sendSealed: false, sendCertificate: false).Seal();

        var result = await Issue(kit);

        Assert.Equal("Signature.Delivery.NothingToDeliver", result.Error.Code);
    }

    // ------------------------------- Página pública -------------------------------

    [Fact]
    public async Task An_active_link_lists_the_files_and_each_download_is_a_fresh_short_url()
    {
        var (kit, token, delivery) = await IssuedFirstDelivery();

        var view = await GetPublicDeliveryHandler.Handle(new GetPublicDeliveryQuery(token), kit.Deliveries, kit.Settings, CancellationToken.None);
        Assert.Equal("Active", view.Value.Status);
        Assert.Equal("a***a@example.com", view.Value.MaskedEmail);
        Assert.Equal(2, view.Value.Items.Count);

        var link = await OpenDeliveryFileHandler.Handle(
            new OpenDeliveryFileCommand(token, view.Value.Items[0].Id),
            kit.Deliveries,
            kit.Storage,
            new NoOpUnitOfWork(),
            NullLogger<OpenDeliveryFileCommand>.Instance,
            CancellationToken.None
        );
        Assert.True(link.IsSuccess);
        Assert.Single(kit.Storage.Presigned);
        Assert.Equal(1, delivery.AccessCount);
    }

    [Fact]
    public async Task Under_another_offices_subdomain_the_link_does_not_exist()
    {
        var (kit, token, _) = await IssuedFirstDelivery();

        var view = await GetPublicDeliveryHandler.Handle(
            new GetPublicDeliveryQuery(token, HostTenantId: Guid.NewGuid()),
            kit.Deliveries,
            kit.Settings,
            CancellationToken.None
        );

        Assert.Equal("Signature.Delivery.NotFound", view.Error.Code);
    }

    [Fact]
    public async Task An_active_link_cannot_be_reissued_publicly()
    {
        var (kit, token, _) = await IssuedFirstDelivery();

        var result = await Reissue(kit, token);

        Assert.Equal("Signature.Delivery.StillActive", result.Error.Code);
    }

    [Fact]
    public async Task A_revoked_link_hides_its_files_and_cannot_be_renewed_from_the_page()
    {
        var (kit, token, delivery) = await IssuedFirstDelivery();
        delivery.Revoke(Guid.NewGuid(), DateTime.UtcNow);

        var view = await GetPublicDeliveryHandler.Handle(new GetPublicDeliveryQuery(token), kit.Deliveries, kit.Settings, CancellationToken.None);
        var reissue = await Reissue(kit, token);

        Assert.Equal("Revoked", view.Value.Status);
        Assert.Empty(view.Value.Items);
        Assert.False(view.Value.CanRequestNewLink);
        Assert.Equal("Signature.Delivery.ReissueNotAllowed", reissue.Error.Code);
    }

    // ------------------------------------------------------------------

    private static SignedDocumentDelivery Issue(DeliveryToken token, TimeSpan lifetime, DateTime now) =>
        SignedDocumentDelivery
            .Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "ana@example.com", DeliveryPurpose.Completed, [AnyItem], token, lifetime, now)
            .Value;

    private static Task HandleAvailable(DeliveryTestKit kit, Guid fileId) =>
        SignedDocumentsReadyConsumer.Handle(
            new BuildingBlocks.Messaging.CloudStorageIntegrationEvents.FileAvailableIntegrationEvent
            {
                TenantId = kit.Request.TenantId,
                CorrelationId = "file-available",
                FileId = fileId,
                ObjectKey = "k",
                ContentType = "application/pdf",
                SizeBytes = 1024,
                ChecksumSha256 = new string('c', 64),
                CreatedBy = kit.Request.CreatedByUserId,
            },
            new SingleRequestRepository(kit.Request),
            kit.DeliverableFiles(),
            kit.Issuer(),
            new NoOpUnitOfWork(),
            new FixedCorrelation(),
            NullLogger<SignatureRequest>.Instance,
            CancellationToken.None
        );

    private static Task<BuildingBlocks.Results.Result<IReadOnlyList<SignedDocumentDeliveryResponse>>> Issue(DeliveryTestKit kit) =>
        IssueDeliveriesHandler.Handle(
            new IssueDeliveriesCommand(kit.Request.TenantId, kit.Request.Id, Guid.NewGuid()),
            new SingleRequestRepository(kit.Request),
            kit.Deliveries,
            kit.DeliverableFiles(),
            kit.Issuer(),
            new NoOpUnitOfWork(),
            CancellationToken.None
        );

    private static async Task<(DeliveryTestKit Kit, string Token, SignedDocumentDelivery Delivery)> IssuedFirstDelivery()
    {
        var kit = new DeliveryTestKit().Seal();
        var issued = await kit.Issuer().PrepareCompletedAsync(kit.Request, CancellationToken.None);
        var first = issued.Single(delivery => delivery.Delivery.SignerId == kit.FirstSigner.Id);
        return (kit, first.Token.Plain, first.Delivery);
    }

    private static Task<BuildingBlocks.Results.Result> Reissue(DeliveryTestKit kit, string token) =>
        RequestNewDeliveryLinkHandler.Handle(
            new RequestNewDeliveryLinkCommand(token),
            kit.Deliveries,
            new SingleRequestRepository(kit.Request),
            kit.Settings,
            kit.Issuer(),
            new NoOpUnitOfWork(),
            CancellationToken.None
        );
}

internal sealed class NoOpUnitOfWork : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
}
