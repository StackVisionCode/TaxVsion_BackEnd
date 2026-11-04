using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Messaging;
using TaxVision.Signature.Domain.Audit;
using TaxVision.Signature.Domain.Delivery;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Settings;
using Wolverine;

namespace TaxVision.Signature.Application.Delivery;

/// <summary>
/// Fases: (1) qué archivos recibe cada firmante (documentos sellados y/o certificados según la solicitud),
/// (2) entrega con secreto propio y vigencia de la oficina, (3) auditoría en la cadena de la solicitud,
/// (4) tras el commit, evento con el token cifrado y los adjuntos que caben bajo el tope de la oficina.
/// </summary>
public sealed class SignedDocumentDeliveryIssuer(
    ISignedDocumentDeliveryRepository deliveries,
    ITenantSignatureSettingsRepository settingsRepository,
    IFileMetadataRefRepository files,
    IAuditChainAppender audit,
    IProtectedPayloadCodec codec,
    IMessageBus bus,
    ICorrelationContext correlation
) : ISignedDocumentDeliveryIssuer
{
    private const string CertificateTitle = "Certificate of completion";

    public async Task<IReadOnlyList<IssuedDelivery>> PrepareCompletedAsync(
        SignatureRequest request,
        CancellationToken ct
    )
    {
        var policy = await PolicyForAsync(request.TenantId, ct);
        var now = DateTime.UtcNow;
        var issued = new List<IssuedDelivery>(request.Signers.Count);

        foreach (var signer in request.Signers.OrderBy(signer => signer.Order))
        {
            if (await deliveries.HasCompletedDeliveryAsync(request.TenantId, request.Id, signer.Id, ct))
                continue;

            var items = await CompletedItemsForAsync(request, signer, ct);
            if (items.Count == 0)
                continue;

            var prepared = await PrepareAsync(request, signer, DeliveryPurpose.Completed, items, policy, now, ct);
            if (prepared.IsSuccess)
                issued.Add(prepared.Value);
        }

        return issued;
    }

    public async Task<Result<IssuedDelivery>> PreparePartialCopyAsync(
        SignatureRequest request,
        Signer signer,
        IReadOnlyList<DeliveryItemSpec> items,
        CancellationToken ct
    )
    {
        var policy = await PolicyForAsync(request.TenantId, ct);
        return await PrepareAsync(request, signer, DeliveryPurpose.PartialCopy, items, policy, DateTime.UtcNow, ct);
    }

    public async Task<Result<IssuedDelivery>> PrepareReissueAsync(
        SignedDocumentDelivery delivery,
        Guid? byUserId,
        CancellationToken ct
    )
    {
        var policy = await PolicyForAsync(delivery.TenantId, ct);
        var token = DeliveryToken.Generate();
        var now = DateTime.UtcNow;
        var reissued = delivery.Reissue(token, policy.LinkLifetime, now);
        if (reissued.IsFailure)
            return Result.Failure<IssuedDelivery>(reissued.Error);

        await audit.AppendAsync(
            delivery.TenantId,
            delivery.SignatureRequestId,
            SignatureAuditEventKind.DeliveryReissued,
            now,
            new
            {
                deliveryId = delivery.Id,
                signerId = delivery.SignerId,
                requestedBy = byUserId is null ? "recipient" : "office",
                byUserId,
                expiresAtUtc = delivery.ExpiresAtUtc,
            },
            ct
        );
        return Result.Success(new IssuedDelivery(delivery, token, IsReissue: true));
    }

    public async Task PublishAsync(IssuedDelivery issued, SignatureRequest request, CancellationToken ct)
    {
        var delivery = issued.Delivery;
        var signer = request.Signers.Single(candidate => candidate.Id == delivery.SignerId);
        var policy = await PolicyForAsync(request.TenantId, ct);

        await bus.PublishAsync(
            new SignedDocumentsDeliveryReadyIntegrationEvent
            {
                TenantId = request.TenantId,
                CorrelationId = correlation.CorrelationId,
                SignatureRequestId = request.Id,
                DeliveryId = delivery.Id,
                Purpose = delivery.Purpose.ToString(),
                Signer = new SignerContactSnapshot(
                    signer.Id,
                    delivery.RecipientEmail,
                    signer.FullName.Value,
                    signer.Language,
                    signer.Order,
                    signer.MappedCustomerId,
                    signer.PhoneNumber?.Value,
                    SignerChannelResolver.PreferredChannelFor(signer)
                ),
                Items = delivery
                    .Items.OrderBy(item => item.Order)
                    .Select(item => new DeliveryItemDescriptor(item.Id, item.Title, item.Kind.ToString()))
                    .ToList(),
                ExpiresAtUtc = delivery.ExpiresAtUtc,
                ProtectedToken = codec.Protect(
                    SignatureDeliveryPayloadSchemas.DeliveryLink,
                    SignatureDeliveryPayloadSchemas.BindingFor(request.TenantId, delivery.Id),
                    new SignatureDeliveryLinkPayload(issued.Token.Plain)
                ),
                AvailableInClientPortal = IsInClientPortal(signer),
                AttachmentFileIds = AttachmentsFor(delivery, policy),
                IsReissue = issued.IsReissue,
                CanRequestNewLink = policy.AllowPublicReissue,
                CompletedAtUtc = request.CompletedAtUtc ?? DateTime.UtcNow,
                TotalSignersCount = request.Signers.Count,
                SendSealedToSigners = request.SendSealedDocumentToSigners,
            }
        );
    }

    // ------------------------------------------------------------------

    private async Task<Result<IssuedDelivery>> PrepareAsync(
        SignatureRequest request,
        Signer signer,
        DeliveryPurpose purpose,
        IReadOnlyList<DeliveryItemSpec> items,
        DeliveryPolicy policy,
        DateTime now,
        CancellationToken ct
    )
    {
        var token = DeliveryToken.Generate();
        var created = SignedDocumentDelivery.Issue(
            request.TenantId,
            request.Id,
            signer.Id,
            signer.Email.Value,
            purpose,
            items,
            token,
            policy.LinkLifetime,
            now
        );
        if (created.IsFailure)
            return Result.Failure<IssuedDelivery>(created.Error);

        await deliveries.AddAsync(created.Value, ct);
        await audit.AppendAsync(
            request.TenantId,
            request.Id,
            SignatureAuditEventKind.DeliveryIssued,
            now,
            new
            {
                deliveryId = created.Value.Id,
                signerId = signer.Id,
                purpose = purpose.ToString(),
                files = items.Select(item => new { item.FileId, kind = item.Kind.ToString() }),
                expiresAtUtc = created.Value.ExpiresAtUtc,
            },
            ct
        );
        return Result.Success(new IssuedDelivery(created.Value, token, IsReissue: false));
    }

    // Documentos sellados para todos los firmantes; el certificado por documento solo a quien firmó ese documento.
    private async Task<IReadOnlyList<DeliveryItemSpec>> CompletedItemsForAsync(
        SignatureRequest request,
        Signer signer,
        CancellationToken ct
    )
    {
        var items = new List<DeliveryItemSpec>();
        var documents = request.Documents.OrderBy(document => document.Order).ToList();

        if (request.SendSealedDocumentToSigners)
            foreach (var document in documents.Where(document => document.SealedFileId is not null))
                items.Add(
                    new DeliveryItemSpec(
                        document.SealedFileId!.Value,
                        DeliveryItemKind.SignedDocument,
                        document.Id,
                        document.Title,
                        await SizeOfAsync(request.TenantId, document.SealedFileId!.Value, ct)
                    )
                );

        if (request.GenerateCertificate && request.SendCertificateToSigners)
        {
            if (request.CertificateFileId is { } requestCertificate)
                items.Add(
                    new DeliveryItemSpec(
                        requestCertificate,
                        DeliveryItemKind.Certificate,
                        null,
                        CertificateTitle,
                        await SizeOfAsync(request.TenantId, requestCertificate, ct)
                    )
                );

            var signedDocumentIds = signer.Fields.Select(field => field.DocumentId).ToHashSet();
            foreach (
                var document in documents.Where(document =>
                    document.CertificateFileId is not null && signedDocumentIds.Contains(document.Id)
                )
            )
                items.Add(
                    new DeliveryItemSpec(
                        document.CertificateFileId!.Value,
                        DeliveryItemKind.Certificate,
                        document.Id,
                        $"{CertificateTitle} — {document.Title}",
                        await SizeOfAsync(request.TenantId, document.CertificateFileId!.Value, ct)
                    )
                );
        }

        return items;
    }

    private async Task<long> SizeOfAsync(Guid tenantId, Guid fileId, CancellationToken ct) =>
        (await files.GetByFileIdAsync(tenantId, fileId, ct))?.SizeBytes ?? 0;

    private async Task<DeliveryPolicy> PolicyForAsync(Guid tenantId, CancellationToken ct) =>
        (await settingsRepository.GetByTenantIdAsync(tenantId, ct))?.Delivery ?? DeliveryPolicy.Default();

    // Solo se adjunta si la oficina lo pidió, todos los tamaños se conocen y el total cabe bajo su tope.
    private static IReadOnlyList<Guid> AttachmentsFor(SignedDocumentDelivery delivery, DeliveryPolicy policy)
    {
        if (!policy.AttachDocuments || delivery.Items.Any(item => item.SizeBytes <= 0))
            return [];
        var total = delivery.Items.Sum(item => item.SizeBytes);
        return total <= policy.MaxAttachmentBytes ? delivery.Items.Select(item => item.FileId).ToList() : [];
    }

    // Todo firmante que es cliente tiene los archivos en su portal: el original si es el único cliente,
    // o su copia (CustomerPortalCopier) si firman varios. La copia en curso siempre queda a su nombre.
    private static bool IsInClientPortal(Signer signer) => signer.MappedCustomerId is not null;
}
