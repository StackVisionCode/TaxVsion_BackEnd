using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using Wolverine;

namespace TaxVision.Auth.Application.Offboarding;

/// <summary>Da por incompleta una corrida vencida y avisa a la oficina (lo despacha el barrido).</summary>
public sealed record MarkOffboardingRunIncompleteCommand(Guid TenantId, Guid RunId);

public static class MarkOffboardingRunIncompleteHandler
{
    public static async Task<Result> Handle(
        MarkOffboardingRunIncompleteCommand command,
        IOffboardingRunRepository runs,
        IUserRepository users,
        IOptions<OffboardingTrackingOptions> options,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var run = await runs.GetByIdAsync(command.TenantId, command.RunId, ct);
        if (run is null)
            return Result.Failure(new Error("Offboarding.NotFound", "Offboarding run not found."));

        var now = DateTime.UtcNow;
        var marked = run.MarkIncomplete(now, options.Value.Timeout);
        if (marked.IsFailure)
            return marked;

        var user = await users.GetByIdAsync(run.UserId, ct);
        var notice = new UserOffboardingIncompleteIntegrationEvent
        {
            TenantId = run.TenantId,
            CorrelationId = correlation.CorrelationId,
            UserId = run.UserId,
            UserDisplayName = DisplayName(user?.Name, user?.LastName),
            StartedAtUtc = run.StartedAtUtc,
            ExpectedSteps = run.ExpectedCount,
            CompletedSteps = run.CompletedCount,
            PendingSteps = run
                .PendingSteps.Select(s => new PendingOffboardingStep(
                    s.Service,
                    s.Step,
                    OffboardingParticipants.ModuleOf(s.Service)
                ))
                .ToArray(),
        };

        // Guardar antes de publicar: un reintento tras un choque no deja salir un aviso de un intento revertido.
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(notice);
        return Result.Success();
    }

    private static string DisplayName(string? name, string? lastName)
    {
        var full = $"{name} {lastName}".Trim();
        return full.Length == 0 ? "A former team member" : full;
    }
}
