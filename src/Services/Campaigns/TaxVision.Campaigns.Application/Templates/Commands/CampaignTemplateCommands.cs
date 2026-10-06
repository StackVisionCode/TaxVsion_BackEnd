using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Campaigns.Application.Templates.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Templates;

namespace TaxVision.Campaigns.Application.Templates.Commands;

/// <summary>Contenido de un canal enviado por el cliente al crear/editar una plantilla.</summary>
public sealed record TemplateContentInput(string Channel, string? Subject, string? Title, string Body);

/// <summary>Aplica (upsert) el contenido por canal sobre una plantilla, reusado por create y update.</summary>
public static class TemplateContentApplier
{
    public static Result Apply(CampaignTemplate template, IReadOnlyList<TemplateContentInput>? contents)
    {
        if (contents is null)
            return Result.Success();
        foreach (var input in contents)
        {
            if (
                !Enum.TryParse<CampaignChannel>(input.Channel, ignoreCase: true, out var channel)
                || channel == CampaignChannel.None
            )
                return Result.Failure(CampaignTemplateErrors.ContentChannelInvalid);
            var set = template.SetChannelContent(channel, input.Subject, input.Title, input.Body);
            if (set.IsFailure)
                return set;
        }
        return Result.Success();
    }
}

// ─────────────────────────── Create ───────────────────────────

public sealed record CreateCampaignTemplateCommand(
    Guid TenantId,
    Guid CreatedByUserId,
    string Name,
    string? Description,
    CampaignChannel Channels,
    IReadOnlyList<TemplateContentInput>? Contents = null
);

public static class CreateCampaignTemplateHandler
{
    public static async Task<Result<CampaignTemplateResponse>> Handle(
        CreateCampaignTemplateCommand command,
        ICampaignTemplateRepository templates,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var result = CampaignTemplate.Create(
            command.TenantId,
            command.CreatedByUserId,
            command.Name,
            command.Description,
            command.Channels
        );
        if (result.IsFailure)
            return Result.Failure<CampaignTemplateResponse>(result.Error);

        var template = result.Value;
        var applied = TemplateContentApplier.Apply(template, command.Contents);
        if (applied.IsFailure)
            return Result.Failure<CampaignTemplateResponse>(applied.Error);

        await templates.AddAsync(template, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(CampaignTemplateResponse.From(template));
    }
}

// ─────────────────────────── Update ───────────────────────────

public sealed record UpdateCampaignTemplateCommand(
    Guid TenantId,
    Guid TemplateId,
    string Name,
    string? Description,
    CampaignChannel Channels,
    IReadOnlyList<TemplateContentInput>? Contents = null
);

public static class UpdateCampaignTemplateHandler
{
    public static async Task<Result<CampaignTemplateResponse>> Handle(
        UpdateCampaignTemplateCommand command,
        ICampaignTemplateRepository templates,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var template = await templates.GetByIdAsync(command.TenantId, command.TemplateId, ct);
        if (template is null)
            return Result.Failure<CampaignTemplateResponse>(CampaignTemplateErrors.NotFound);

        var updated = template.Update(command.Name, command.Description, command.Channels);
        if (updated.IsFailure)
            return Result.Failure<CampaignTemplateResponse>(updated.Error);
        var applied = TemplateContentApplier.Apply(template, command.Contents);
        if (applied.IsFailure)
            return Result.Failure<CampaignTemplateResponse>(applied.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(CampaignTemplateResponse.From(template));
    }
}

// ─────────────────────────── Delete ───────────────────────────

public sealed record DeleteCampaignTemplateCommand(Guid TenantId, Guid TemplateId);

public static class DeleteCampaignTemplateHandler
{
    public static async Task<Result> Handle(
        DeleteCampaignTemplateCommand command,
        ICampaignTemplateRepository templates,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var template = await templates.GetByIdAsync(command.TenantId, command.TemplateId, ct);
        if (template is null)
            return Result.Failure(CampaignTemplateErrors.NotFound);

        templates.Remove(template); // contenido cae por cascade
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
