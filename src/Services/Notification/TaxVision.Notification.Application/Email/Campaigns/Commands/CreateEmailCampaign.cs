using System.Text.Json;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Campaigns;

namespace TaxVision.Notification.Application.Email.Campaigns.Commands;

/// <summary>Crea una campaña en borrador con destinatarios explícitos (integración con Customer Service en el futuro).</summary>
public sealed record CreateEmailCampaignCommand(
    Guid TenantId,
    Guid? CreatedByUserId,
    string Name,
    CampaignType Type,
    Guid TemplateId,
    IReadOnlyList<CampaignRecipientInput> Recipients
);

public static class CreateEmailCampaignHandler
{
    public static async Task<Result<EmailCampaignResponse>> Handle(
        CreateEmailCampaignCommand command,
        IEmailCampaignRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (command.TenantId == Guid.Empty)
            return Result.Failure<EmailCampaignResponse>(new Error("Campaign.Tenant", "Tenant is required."));

        if (command.Recipients is null || command.Recipients.Count == 0)
            return Result.Failure<EmailCampaignResponse>(
                new Error("Campaign.Recipients", "At least one recipient is required.")
            );

        var recipients = command
            .Recipients.Select(r =>
                (r.Address, r.Name, (string?)JsonSerializer.Serialize(r.Variables ?? new Dictionary<string, string?>()))
            )
            .ToList();

        var result = EmailCampaign.Create(
            command.TenantId,
            command.Name,
            command.Type,
            command.TemplateId,
            recipients,
            command.CreatedByUserId
        );
        if (result.IsFailure)
            return Result.Failure<EmailCampaignResponse>(result.Error);

        await repository.AddAsync(result.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(EmailCampaignMapper.ToResponse(result.Value));
    }
}
