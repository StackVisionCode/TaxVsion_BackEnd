using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

public static class GetPublicIntakeFormAccessHandler
{
    public static async Task<Result<PublicIntakeFormAccessResponse>> Handle(
        GetPublicIntakeFormAccessQuery query,
        IFormIntakeLinkRepository links,
        IFormExternalAccessSecurity security,
        IFormPublicTenantHostValidator hosts,
        IClientFormsPublicCapabilityEvaluator capabilities,
        IFormTemplateVersionRepository versions,
        IConsentNoticeRepository notices,
        IOptions<ClientFormsOptions> options,
        CancellationToken ct
    )
    {
        var link = await PublicIntakeFormAccess.LoadAsync(
            query.IntakeToken,
            query.RequestHost,
            query.NowUtc,
            links,
            security,
            hosts,
            capabilities,
            ct
        );
        if (link is null)
            return Result.Failure<PublicIntakeFormAccessResponse>(PublicIntakeFormErrors.NotFound);
        var version = await versions.GetByIdAsync(link.TenantId, link.FormTemplateVersionId, ct);
        var entryNotice = await ResolveEntryNoticeAsync(link, notices, ct);
        var bot = options.Value.External.BotProtection;
        return version.IsFailure || entryNotice is null || string.IsNullOrWhiteSpace(bot.SiteKey)
            ? Result.Failure<PublicIntakeFormAccessResponse>(PublicIntakeFormErrors.NotFound)
            : Result.Success(
                new PublicIntakeFormAccessResponse(
                    version.Value.Title,
                    query.RequestHost,
                    EntryNoticeSupport.ToResponse(entryNotice),
                    new PublicFormBotProtectionResponse("turnstile", bot.SiteKey, bot.ExpectedAction)
                )
            );
    }

    private static Task<EntryNoticeSelection?> ResolveEntryNoticeAsync(
        FormIntakeLink link,
        IConsentNoticeRepository notices,
        CancellationToken ct
    ) =>
        EntryNoticeSupport.ResolvePinnedAsync(
            link.TenantId,
            link.EntryNoticeId,
            link.EntryNoticeVersionNumber,
            notices,
            ct
        );
}
