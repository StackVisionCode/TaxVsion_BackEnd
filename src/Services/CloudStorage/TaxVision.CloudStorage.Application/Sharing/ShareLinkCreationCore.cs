using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Domain.Sharing;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Sharing;

/// <summary>Logica de creacion compartida por File y Folder — evita duplicar validacion/persistencia/evento entre los dos handlers.</summary>
internal static class ShareLinkCreationCore
{
    public static async Task<Result<CreatedShareLinkResponse>> CreateAndPersist(
        Guid tenantId,
        Guid actorId,
        bool actorHasManagePermission,
        Guid resourceId,
        ShareResourceType resourceType,
        ShareVisibility visibility,
        SharePermission permission,
        string? password,
        DateTime? expiresAtUtc,
        int? maxAccessCount,
        bool isRecursive,
        bool appliesToFutureItems,
        IReadOnlyList<Guid> recipientUserIds,
        IReadOnlyList<Guid> recipientCustomerIds,
        IReadOnlyList<string> recipientEmails,
        string resourceName,
        string recipientLanguage,
        RequestAuditContext auditContext,
        IShareLinkRepository shares,
        IStorageLimitRepository limits,
        IShareLinkPasswordHasher passwordHasher,
        IStorageAuditRepository audit,
        ISystemClock clock,
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        CancellationToken ct,
        bool suppressRecipientEmails = false
    )
    {
        var limit = await limits.GetAsync(tenantId, ct);
        var policyResult = ValidatePolicy(
            actorHasManagePermission,
            permission,
            visibility,
            recipientUserIds,
            recipientCustomerIds,
            recipientEmails,
            password,
            expiresAtUtc,
            maxAccessCount,
            clock.UtcNow,
            enforceLinkGuardrails: true,
            limit
        );
        if (policyResult.IsFailure)
            return Result.Failure<CreatedShareLinkResponse>(policyResult.Error);

        // Sin fecha ni tope elegidos: los valores por defecto de la oficina (el tope solo en enlaces externos).
        var effectiveMaxAccessCount = maxAccessCount ?? (visibility.IsExternal() ? limit?.MaxShareDownloads : null);

        var created = ShareLink.Create(
            Guid.NewGuid(),
            tenantId,
            resourceId,
            resourceType,
            visibility,
            permission,
            string.IsNullOrWhiteSpace(password) ? null : passwordHasher.Hash(password),
            expiresAtUtc,
            effectiveMaxAccessCount,
            actorId,
            clock.UtcNow,
            isRecursive,
            appliesToFutureItems,
            limit?.DefaultShareLifetimeDays ?? ShareLink.DefaultLifetimeDays
        );
        if (created.IsFailure)
            return Result.Failure<CreatedShareLinkResponse>(created.Error);

        var (link, plainToken) = created.Value;
        foreach (var userId in recipientUserIds)
            link.AddUserRecipient(userId);
        foreach (var customerId in recipientCustomerIds)
            link.AddCustomerRecipient(customerId);
        foreach (var email in recipientEmails)
            link.AddExternalRecipient(email);

        shares.Add(link);
        audit.Add(
            StorageAccessLog.Create(
                tenantId,
                resourceId,
                actorId,
                "share.create",
                "success",
                auditContext.IpAddress,
                auditContext.UserAgent,
                auditContext.CorrelationId,
                $"resourceType={resourceType};visibility={visibility};permission={permission}",
                clock.UtcNow
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new ShareLinkCreatedIntegrationEvent
            {
                TenantId = tenantId,
                ShareLinkId = link.Id,
                ResourceId = resourceId,
                ResourceType = resourceType.ToString(),
                Visibility = visibility.ToString(),
                CreatedByUserId = actorId,
                CorrelationId = auditContext.CorrelationId,
            }
        );

        // Magic-link por destinatario externo: un evento por email para que Notification emaile la
        // página pública `/s/{token}?email=`. Solo para archivos con visibility ExternalRecipients.
        // Se omite cuando el llamador ya entrega el enlace en su propio correo (suppressRecipientEmails),
        // para no mandar el email genérico de CloudStorage por duplicado.
        if (
            resourceType == ShareResourceType.File
            && visibility == ShareVisibility.ExternalRecipients
            && !suppressRecipientEmails
        )
        {
            var language = string.IsNullOrWhiteSpace(recipientLanguage) ? "En" : recipientLanguage;
            foreach (var email in recipientEmails)
                await bus.PublishAsync(
                    new ShareLinkExternalRecipientInvitedIntegrationEvent
                    {
                        TenantId = tenantId,
                        ShareLinkId = link.Id,
                        FileId = resourceId,
                        Email = email,
                        PlainToken = plainToken,
                        FileName = resourceName,
                        Permission = permission.ToString(),
                        ExpiresAtUtc = link.ExpiresAtUtc,
                        Language = language,
                        CorrelationId = auditContext.CorrelationId,
                    }
                );
        }

        return Result.Success(
            new CreatedShareLinkResponse(ShareLinkResponseMapper.Map(link, clock.UtcNow), plainToken)
        );
    }

    internal static Result ValidatePolicy(
        bool actorHasManagePermission,
        SharePermission permission,
        ShareVisibility visibility,
        IReadOnlyList<Guid> recipientUserIds,
        IReadOnlyList<Guid> recipientCustomerIds,
        IReadOnlyList<string> recipientEmails,
        string? password,
        DateTime? expiresAtUtc,
        int? maxAccessCount,
        DateTime nowUtc,
        bool enforceLinkGuardrails,
        TenantStorageLimit? limit
    )
    {
        if (permission is SharePermission.Upload or SharePermission.EditMetadata && !actorHasManagePermission)
            return Result.Failure(ShareErrors.ElevatedPermissionRequiresManage);

        // §20.4 del plan: un link sin autenticación (Public o ExternalLink) nunca puede otorgar
        // Upload/EditMetadata (sería escritura anónima).
        if (visibility.IsUnauthenticatedLink() && permission is SharePermission.Upload or SharePermission.EditMetadata)
            return Result.Failure(ShareErrors.ElevatedPermissionNotAllowedOnPublicLink);

        if (visibility == ShareVisibility.Public && limit is not { AllowPublicShareLinks: true })
            return Result.Failure(ShareErrors.PublicSharingDisabled);
        if (visibility == ShareVisibility.ExternalLink && limit is not { AllowLinkOnlyExternalShares: true })
            return Result.Failure(ShareErrors.LinkSharingDisabled);

        // Reglas de la oficina solo al crear (no al cambiar el permiso de un enlace ya existente).
        if (enforceLinkGuardrails && limit is not null)
        {
            var passwordCheck = limit.CheckSharePassword(password);
            if (passwordCheck.IsFailure)
                return passwordCheck;
            if (visibility.IsExternal())
            {
                var externalCheck = limit.CheckNewExternalShare(
                    visibility,
                    password,
                    expiresAtUtc,
                    maxAccessCount,
                    nowUtc
                );
                if (externalCheck.IsFailure)
                    return externalCheck;
            }
        }

        var needsRecipients = visibility is ShareVisibility.SpecificUsers or ShareVisibility.ExternalRecipients;
        var hasRecipients = recipientUserIds.Count > 0 || recipientCustomerIds.Count > 0 || recipientEmails.Count > 0;
        if (needsRecipients && !hasRecipients)
            return Result.Failure(ShareErrors.RecipientsRequired);

        return Result.Success();
    }
}
