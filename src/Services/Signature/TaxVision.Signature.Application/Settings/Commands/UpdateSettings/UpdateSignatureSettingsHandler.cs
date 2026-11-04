using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Limits;
using TaxVision.Signature.Domain.Settings;
using Wolverine;

namespace TaxVision.Signature.Application.Settings.Commands.UpdateSettings;

public static class UpdateSignatureSettingsHandler
{
    public static async Task<Result> Handle(
        UpdateSignatureSettingsCommand cmd,
        ITenantSignatureSettingsRepository repository,
        ISignatureLimitsResolver limits,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var settings = await repository.GetByTenantIdAsync(cmd.TenantId, ct);
        if (settings is null)
            return Result.Failure(new Error("Signature.Settings.NotFound", "No settings found for this tenant."));

        // Validar contra los techos vigentes (plan + excepción) ANTES de aplicar cambios.
        var plan = (await limits.ResolveAsync(cmd.TenantId, ct)).Ceilings;

        var disallowedChannels = cmd.AllowedChannels & ~plan.AllowedChannels;
        if (disallowedChannels != VerificationChannel.None)
            return Result.Failure(
                new Error(
                    "Signature.Settings.ChannelNotInPlan",
                    $"Your plan does not allow the following channel(s): {disallowedChannels}."
                )
            );

        if ((cmd.DefaultChannel & ~plan.AllowedChannels) != VerificationChannel.None)
            return Result.Failure(
                new Error(
                    "Signature.Settings.ChannelNotInPlan",
                    $"Your plan does not allow the default channel '{cmd.DefaultChannel}'."
                )
            );

        if (cmd.DefaultTokenExpirationHours > plan.MaxTokenHours)
            return Result.Failure(
                new Error(
                    "Signature.Settings.TokenExpirationExceedsPlan",
                    $"Your plan allows a maximum token expiration of {plan.MaxTokenHours} hours."
                )
            );

        if (cmd.MaxPdfBytes > plan.MaxPdfBytes)
            return Result.Failure(
                new Error(
                    "Signature.Settings.PdfBytesExceedsPlan",
                    $"Your plan allows a maximum PDF size of {plan.MaxPdfBytes} bytes."
                )
            );

        if (cmd.MaxPagesPerDocument > plan.MaxPagesPerDocument)
            return Result.Failure(
                new Error(
                    "Signature.Settings.PagesExceedsPlan",
                    $"Your plan allows a maximum of {plan.MaxPagesPerDocument} pages per document."
                )
            );

        if (cmd.AllowPurge && !plan.PurgeAllowed)
            return Result.Failure(
                new Error("Signature.Settings.PurgeNotInPlan", "Your plan does not allow enabling document purge.")
            );

        // Enable requested channels first so there is always at least one active
        // before we disable the ones that were removed.
        foreach (VerificationChannel ch in Enum.GetValues<VerificationChannel>())
        {
            if (ch == VerificationChannel.None)
                continue;
            if (cmd.AllowedChannels.HasFlag(ch))
            {
                var r = settings.AllowVerificationChannel(ch);
                if (r.IsFailure)
                    return r;
            }
        }

        // Set the default channel while all requested channels are already active.
        {
            var r = settings.SetDefaultVerificationChannel(cmd.DefaultChannel);
            if (r.IsFailure)
                return r;
        }

        // Remove channels not present in the new set (safe: at least one is still enabled).
        foreach (VerificationChannel ch in Enum.GetValues<VerificationChannel>())
        {
            if (ch == VerificationChannel.None)
                continue;
            if (!cmd.AllowedChannels.HasFlag(ch))
            {
                var r = settings.DisallowVerificationChannel(ch);
                if (r.IsFailure)
                    return r;
            }
        }

        {
            var r = settings.ChangeDefaultTokenExpiration(cmd.DefaultTokenExpirationHours, plan);
            if (r.IsFailure)
                return r;
        }

        if (cmd.RemindersEnabledByDefault)
            settings.EnableAutomaticReminders();
        else
            settings.DisableAutomaticReminders();

        {
            var r = settings.SetDefaultReminderInterval(cmd.DefaultReminderIntervalHours);
            if (r.IsFailure)
                return r;
        }

        if (cmd.GenerateCertificateByDefault)
            settings.EnableCertificateOfCompletion();
        else
            settings.DisableCertificateOfCompletion();

        if (cmd.AllowEmployeeOwnSignature)
            settings.EnableEmployeeOwnSignature();
        else
            settings.DisableEmployeeOwnSignature();

        // F7 — nuevos defaults; solo se aplican si vinieron en el body.
        if (cmd.SendPartialCopyDefault is { } partialDefault)
            settings.SetSendPartialCopyDefault(partialDefault);
        if (cmd.PartialCopyDefaultAudienceKind is { } audienceKind)
            settings.SetPartialCopyDefaultAudienceKind(audienceKind);
        if (cmd.SendSealedDocumentDefault is { } sealedDefault)
            settings.SetSendSealedDocumentDefault(sealedDefault);
        if (cmd.ExpirationEnabledByDefault is { } expDefault)
            settings.SetExpirationEnabledByDefault(expDefault);
        if (cmd.SendCertificateToSignersDefault is { } sendCertDefault)
            settings.SetSendCertificateToSignersDefault(sendCertDefault);
        if (cmd.RequiresConsentByDefault is { } consentDefault)
            settings.SetRequiresConsentByDefault(consentDefault);
        if (cmd.ShowSignatureFrame is { } showFrame)
            settings.SetShowSignatureFrame(showFrame);
        if (cmd.DateStampFormat is { } dateFormat)
            settings.SetDateStampFormat(dateFormat);
        if (cmd.Delivery is { } delivery)
        {
            var deliveryPolicy = DeliveryPolicy.Create(
                delivery.LinkLifetimeDays,
                delivery.AllowPublicReissue,
                delivery.AttachDocuments,
                delivery.MaxAttachmentBytes
            );
            if (deliveryPolicy.IsFailure)
                return Result.Failure(deliveryPolicy.Error);
            settings.ReplaceDeliveryPolicy(deliveryPolicy.Value);
        }

        var pdfResult = DocumentLimits.Default().WithMaxPdfBytes(cmd.MaxPdfBytes);
        if (pdfResult.IsFailure)
            return Result.Failure(pdfResult.Error);

        var pagesResult = pdfResult.Value.WithMaxPages(cmd.MaxPagesPerDocument);
        if (pagesResult.IsFailure)
            return Result.Failure(pagesResult.Error);

        {
            var r = settings.ReplaceDocumentLimits(pagesResult.Value);
            if (r.IsFailure)
                return r;
        }

        // Solo la purga: los años los fija Cumplimiento y el mínimo del plan se aplica al leerlos.
        settings.SetPurgeAllowed(cmd.AllowPurge);

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new SignatureSettingsUpdatedIntegrationEvent
            {
                CorrelationId = correlation.CorrelationId,
                TenantId = cmd.TenantId,
                ChangedByUserId = cmd.ChangedByUserId,
                UpdatedAtUtc = settings.UpdatedAtUtc,
            }
        );

        return Result.Success();
    }
}
