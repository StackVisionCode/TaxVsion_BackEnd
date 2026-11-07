using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Profiles.Abstractions;
using TaxVision.Tenant.Domain.Profiles;
using Wolverine;

namespace TaxVision.Tenant.Application.Profiles.Commands;

/// <summary>Estado completo del perfil público de la oficina del que llama.</summary>
public sealed record UpdateOfficeProfileCommand(Guid TenantId, Guid ActorId, OfficeProfileDraft Draft);

/// <summary>
/// Guarda el perfil y recién después publica <see cref="TenantProfileChangedIntegrationEvent"/> (outbox).
/// Sin cambios no guarda, no sube la versión ni publica: un PUT repetido es idempotente.
/// </summary>
public static class UpdateOfficeProfileHandler
{
    public static async Task<Result<OfficeProfileResponse>> Handle(
        UpdateOfficeProfileCommand command,
        IOfficeProfileRepository profiles,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var existing = await profiles.GetAsync(command.TenantId, ct);
        var profile = existing ?? OfficeProfile.CreateEmpty(command.TenantId);

        var update = profile.Update(command.Draft, command.ActorId, clock.GetUtcNow().UtcDateTime);
        if (update.IsFailure)
            return Result.Failure<OfficeProfileResponse>(update.Error);

        if (update.Value)
            await PersistAndPublish(profile, isNew: existing is null, profiles, unitOfWork, bus, correlation, ct);

        return Result.Success(OfficeProfileResponses.From(profile));
    }

    private static async Task PersistAndPublish(
        OfficeProfile profile,
        bool isNew,
        IOfficeProfileRepository profiles,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (isNew)
            await profiles.AddAsync(profile, ct);

        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(OfficeProfileChangedEvents.From(profile, correlation.CorrelationId));
    }
}
