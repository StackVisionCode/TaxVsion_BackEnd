using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Settings;
using Wolverine;

namespace TaxVision.Signature.Application.Settings.Commands.UpdateSettings;

public static class UpdateSignatureSettingsHandler
{
    public static async Task<Result> Handle(
        UpdateSignatureSettingsCommand cmd,
        ITenantSignatureSettingsRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var settings = await repository.GetByTenantIdAsync(cmd.TenantId, ct);
        if (settings is null)
            return Result.Failure(new Error("Signature.Settings.NotFound", "No settings found for this tenant."));

        // Validar contra restricciones de plan ANTES de aplicar cambios.
        var plan = settings.PlanConstraints;

        var disallowedChannels = cmd.AllowedChannels & ~plan.AllowedChannels;
        if (disallowedChannels != VerificationChannel.None)
            return Result.Failure(new Error(
                "Signature.Settings.ChannelNotInPlan",
                $"Your plan does not allow the following channel(s): {disallowedChannels}."));

        if ((cmd.DefaultChannel & ~plan.AllowedChannels) != VerificationChannel.None)
            return Result.Failure(new Error(
                "Signature.Settings.ChannelNotInPlan",
                $"Your plan does not allow the default channel '{cmd.DefaultChannel}'."));

        if (cmd.DefaultTokenExpirationHours > plan.MaxTokenExpirationHours)
            return Result.Failure(new Error(
                "Signature.Settings.TokenExpirationExceedsPlan",
                $"Your plan allows a maximum token expiration of {plan.MaxTokenExpirationHours} hours."));

        if (cmd.MaxPdfBytes > plan.MaxAllowedPdfBytes)
            return Result.Failure(new Error(
                "Signature.Settings.PdfBytesExceedsPlan",
                $"Your plan allows a maximum PDF size of {plan.MaxAllowedPdfBytes} bytes."));

        if (cmd.MaxImageBytes > plan.MaxAllowedImageBytes)
            return Result.Failure(new Error(
                "Signature.Settings.ImageBytesExceedsPlan",
                $"Your plan allows a maximum image size of {plan.MaxAllowedImageBytes} bytes."));

        if (cmd.MaxPagesPerDocument > plan.MaxAllowedPages)
            return Result.Failure(new Error(
                "Signature.Settings.PagesExceedsPlan",
                $"Your plan allows a maximum of {plan.MaxAllowedPages} pages per document."));

        if (cmd.RetentionYears < plan.MinRetentionYears)
            return Result.Failure(new Error(
                "Signature.Settings.RetentionBelowPlan",
                $"Your plan requires a minimum retention of {plan.MinRetentionYears} years."));

        if (cmd.AllowPurge && !plan.PurgeAllowed)
            return Result.Failure(new Error(
                "Signature.Settings.PurgeNotInPlan",
                "Your plan does not allow enabling document purge."));

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
            var r = settings.ChangeDefaultTokenExpiration(cmd.DefaultTokenExpirationHours);
            if (r.IsFailure)
                return r;
        }

        if (cmd.RemindersEnabledByDefault)
            settings.EnableAutomaticReminders();
        else
            settings.DisableAutomaticReminders();

        if (cmd.GenerateCertificateByDefault)
            settings.EnableCertificateOfCompletion();
        else
            settings.DisableCertificateOfCompletion();

        var pdfResult = DocumentLimits.Default().WithMaxPdfBytes(cmd.MaxPdfBytes);
        if (pdfResult.IsFailure)
            return Result.Failure(pdfResult.Error);

        var imgResult = pdfResult.Value.WithMaxImageBytes(cmd.MaxImageBytes);
        if (imgResult.IsFailure)
            return Result.Failure(imgResult.Error);

        var pagesResult = imgResult.Value.WithMaxPages(cmd.MaxPagesPerDocument);
        if (pagesResult.IsFailure)
            return Result.Failure(pagesResult.Error);

        {
            var r = settings.ReplaceDocumentLimits(pagesResult.Value);
            if (r.IsFailure)
                return r;
        }

        var retentionResult = RetentionPolicy.Default().WithYears(cmd.RetentionYears);
        if (retentionResult.IsFailure)
            return Result.Failure(retentionResult.Error);

        var policy = cmd.AllowPurge
            ? retentionResult.Value.WithPurgeAllowed()
            : retentionResult.Value.WithPurgeBlocked();

        {
            var r = settings.ReplaceRetentionPolicy(policy);
            if (r.IsFailure)
                return r;
        }

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(new SignatureSettingsUpdatedIntegrationEvent
        {
            TenantId        = cmd.TenantId,
            ChangedByUserId = cmd.ChangedByUserId,
            UpdatedAtUtc    = settings.UpdatedAtUtc,
        });

        return Result.Success();
    }
}
