using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Abstractions;
using TaxVision.Tenant.Application.Compliance.Abstractions;
using TaxVision.Tenant.Domain.Compliance;
using TaxVision.Tenant.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Tenant.Application.Compliance.Commands;

/// <summary>
/// Estado completo del cumplimiento. <paramref name="Efin"/>: null = no tocar, vacío = borrar, si no, el
/// EFIN nuevo (6 dígitos) en claro, que se cifra antes de llegar al agregado.
/// </summary>
public sealed record UpdateOfficeComplianceCommand(
    Guid TenantId,
    Guid ActorId,
    string? Efin,
    OfficeComplianceDraft Draft
);

/// <summary>
/// Valida el EFIN y el responsable de seguridad antes de tocar el agregado, que valida el resto; nada se
/// aplica si algo falla. Lo único que sale de Tenant son los años de retención: si cambiaron (también a
/// null) publica <see cref="TenantRecordRetentionChangedIntegrationEvent"/> después de guardar.
/// </summary>
public static class UpdateOfficeComplianceHandler
{
    public static async Task<Result<OfficeComplianceResponse>> Handle(
        UpdateOfficeComplianceCommand command,
        IOfficeComplianceRepository compliances,
        IUserPermissionsProjectionRepository users,
        IEfinProtector protector,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var existing = await compliances.GetAsync(command.TenantId, ct);
        var compliance = existing ?? OfficeCompliance.CreateEmpty(command.TenantId);
        var previousRetention = compliance.RetentionYears;

        var efin = ResolveEfinChange(command.Efin, compliance.Efin, protector);
        if (efin.IsFailure)
            return Result.Failure<OfficeComplianceResponse>(efin.Error);

        var coordinator = await EnsureCoordinatorBelongsToOffice(command, compliance, users, ct);
        if (coordinator.IsFailure)
            return Result.Failure<OfficeComplianceResponse>(coordinator.Error);

        var update = compliance.Update(command.Draft, efin.Value, command.ActorId, clock.GetUtcNow().UtcDateTime);
        if (update.IsFailure)
            return Result.Failure<OfficeComplianceResponse>(update.Error);

        if (update.Value)
        {
            if (existing is null)
                await compliances.AddAsync(compliance, ct);
            await unitOfWork.SaveChangesAsync(ct);

            // Guardar antes de publicar: un guardado que falla no deja salir el evento.
            if (compliance.RetentionYears != previousRetention)
                await bus.PublishAsync(RetentionChanged(compliance, correlation.CorrelationId));
        }

        return Result.Success(OfficeComplianceResponse.From(compliance));
    }

    private static TenantRecordRetentionChangedIntegrationEvent RetentionChanged(
        OfficeCompliance compliance,
        string correlationId
    ) =>
        new TenantRecordRetentionChangedIntegrationEvent
        {
            TenantId = compliance.TenantId,
            OfficeTenantId = compliance.TenantId,
            RetentionYears = compliance.RetentionYears,
            UpdatedAtUtc =
                compliance.UpdatedAtUtc
                ?? throw new InvalidOperationException("Retention is published only after it has been saved."),
            CorrelationId = correlationId,
        };

    private static Result<EfinChange> ResolveEfinChange(string? raw, EncryptedEfin? current, IEfinProtector protector)
    {
        if (raw is null)
            return Result.Success(EfinChange.Keep);
        if (string.IsNullOrWhiteSpace(raw))
            return Result.Success(EfinChange.Clear);

        var efin = Efin.Create(raw);
        if (efin.IsFailure)
            return Result.Failure<EfinChange>(efin.Error);

        // Cifrar dos veces el mismo EFIN da bytes distintos: se compara en claro para no inventar un cambio.
        if (current is not null && protector.Reveal(current) == efin.Value.Value)
            return Result.Success(EfinChange.Keep);

        return Result.Success(EfinChange.Replace(protector.Protect(efin.Value)));
    }

    // Solo se comprueba cuando cambia: un responsable ya guardado no bloquea editar el resto.
    private static async Task<Result> EnsureCoordinatorBelongsToOffice(
        UpdateOfficeComplianceCommand command,
        OfficeCompliance compliance,
        IUserPermissionsProjectionRepository users,
        CancellationToken ct
    )
    {
        if (
            command.Draft.SecurityCoordinatorUserId is not { } userId
            || userId == Guid.Empty
            || userId == compliance.SecurityCoordinatorUserId
        )
            return Result.Success();

        var user = await users.GetAsync(command.TenantId, userId, ct);
        return user is { IsActive: true }
            ? Result.Success()
            : Result.Failure(OfficeComplianceErrors.SecurityCoordinator);
    }
}
