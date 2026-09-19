using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Campaigns.Application.Campaigns.Abstractions;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;
using Wolverine;

namespace TaxVision.Campaigns.Application.Runs.Commands;

/// <summary>Una persona destinataria del envío (se expande a una unidad por canal de la campaña).</summary>
public sealed record StartRunRecipient(string ContactRef, string? Email, string? PhoneE164);

/// <summary>
/// Arranca una ejecución (<c>send-now</c>) de una campaña sobre una lista de destinatarios. Slice 2:
/// audiencia manual explícita (Customer/Contactos/segmentos y el Scheduler llegan después). Cada
/// persona × cada canal de la campaña = una unidad; se hace fan-out del contrato
/// <c>campaign.dispatch.requested.v1</c> por unidad. SIN dinero.
/// </summary>
public sealed record StartCampaignRunCommand(
    Guid TenantId,
    Guid CampaignId,
    Guid TriggeredByUserId,
    IReadOnlyList<StartRunRecipient> Recipients
);

public static class StartCampaignRunHandler
{
    public static async Task<Result<CampaignRunResponse>> Handle(
        StartCampaignRunCommand command,
        ICampaignRepository campaigns,
        ICampaignRunRepository runs,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var campaign = await campaigns.GetByIdAsync(command.TenantId, command.CampaignId, ct);
        if (campaign is null)
            return Result.Failure<CampaignRunResponse>(CampaignErrors.NotFound);
        if (campaign.Status == CampaignStatus.Archived)
            return Result.Failure<CampaignRunResponse>(CampaignErrors.Archived);

        var units = ExpandUnits(campaign.Channels, command.Recipients);

        var runResult = CampaignRun.Start(command.TenantId, campaign.Id, command.TriggeredByUserId, "Manual", units);
        if (runResult.IsFailure)
            return Result.Failure<CampaignRunResponse>(runResult.Error);

        var run = runResult.Value;
        await runs.AddAsync(run, ct);

        await bus.PublishAsync(
            new CampaignRunStartedIntegrationEvent
            {
                TenantId = run.TenantId,
                CorrelationId = correlation.CorrelationId,
                CampaignId = run.CampaignId,
                RunId = run.Id,
                RecipientCount = run.RecipientCount,
                TriggeredBy = run.TriggerKind,
            }
        );

        foreach (var recipient in run.Recipients.Where(r => r.State == DispatchState.Pending).ToList())
        {
            await bus.PublishAsync(
                new CampaignDispatchRequestedIntegrationEvent
                {
                    TenantId = run.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    CampaignId = run.CampaignId,
                    RunId = run.Id,
                    RecipientId = recipient.Id,
                    AttemptNo = recipient.AttemptNo,
                    DispatchId = recipient.DispatchId,
                    Channel = recipient.Channel.ToString(),
                    ContactRef = recipient.ContactRef,
                    Email = recipient.Email,
                    PhoneE164 = recipient.PhoneE164,
                    // Slice 3: contenido inline desde la definición (snapshot inmutable + Scribe = fase posterior).
                    Subject = campaign.Subject,
                    Body = campaign.Message,
                }
            );
            run.MarkDispatched(recipient.Id);
        }

        // Caso borde: todas las unidades quedaron Skipped al materializar → el run ya cerró en Start.
        if (run.Status is CampaignRunStatus.Completed or CampaignRunStatus.PartiallyFailed or CampaignRunStatus.Failed)
            await PublishCompletedAsync(bus, run, correlation.CorrelationId);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(CampaignRunResponse.From(run));
    }

    private static IReadOnlyCollection<RunRecipientDraft> ExpandUnits(
        CampaignChannel channels,
        IReadOnlyList<StartRunRecipient> recipients
    )
    {
        var activeChannels = Enum.GetValues<CampaignChannel>()
            .Where(c => c != CampaignChannel.None && channels.HasFlag(c))
            .ToList();

        var units = new List<RunRecipientDraft>();
        foreach (var recipient in recipients)
        foreach (var channel in activeChannels)
            units.Add(new RunRecipientDraft(recipient.ContactRef, channel, recipient.Email, recipient.PhoneE164));

        return units;
    }

    internal static Task PublishCompletedAsync(IMessageBus bus, CampaignRun run, string correlationId) =>
        bus.PublishAsync(
                new CampaignRunCompletedIntegrationEvent
                {
                    TenantId = run.TenantId,
                    CorrelationId = correlationId,
                    CampaignId = run.CampaignId,
                    RunId = run.Id,
                    TerminalStatus = run.Status.ToString(),
                    RecipientCount = run.RecipientCount,
                    Delivered = run.CounterDelivered,
                    Accepted = run.CounterAccepted,
                    Failed = run.CounterFailed,
                    Skipped = run.CounterSkipped,
                    Unknown = run.CounterUnknown,
                }
            )
            .AsTask();
}
