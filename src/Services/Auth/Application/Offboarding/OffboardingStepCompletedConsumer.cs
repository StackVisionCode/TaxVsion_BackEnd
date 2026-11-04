using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Domain.Offboarding;

namespace TaxVision.Auth.Application.Offboarding;

/// <summary>
/// Registra el acuse de un paso del retiro en su <see cref="OffboardingRun"/>. Idempotente por Service+Step
/// (un acuse repetido no cambia nada) y sin orden asumido: cada acuse toca solo la fila de su paso.
/// Corrida inexistente (retiro anterior a este seguimiento) o paso no esperado: se loguea y se descarta.
/// </summary>
public static class OffboardingStepCompletedConsumer
{
    public static async Task Handle(
        UserOffboardingStepCompletedIntegrationEvent evt,
        IOffboardingRunRepository runs,
        IUnitOfWork unitOfWork,
        ILogger<OffboardingRun> logger,
        CancellationToken ct
    )
    {
        var run = await runs.GetByUserAsync(evt.TenantId, evt.UserId, ct);
        if (run is null)
        {
            logger.LogWarning(
                "Offboarding step {Service}:{Step} for user {UserId} has no offboarding run; ignored.",
                evt.Service,
                evt.Step,
                evt.UserId
            );
            return;
        }

        var completedAt = evt.CompletedAtUtc == default ? DateTime.UtcNow : evt.CompletedAtUtc;
        var result = run.CompleteStep(evt.Service, evt.Step, completedAt);
        if (result.IsFailure)
        {
            logger.LogWarning(
                "Offboarding step {Service}:{Step} for user {UserId} rejected: {Reason}",
                evt.Service,
                evt.Step,
                evt.UserId,
                result.Error.Message
            );
            return;
        }

        if (!result.Value)
            return;

        await unitOfWork.SaveChangesAsync(ct);
        if (run.AllCompleted)
            logger.LogInformation(
                "Offboarding of user {UserId} completed: {Completed}/{Expected} steps confirmed.",
                evt.UserId,
                run.CompletedCount,
                run.ExpectedCount
            );
    }
}
