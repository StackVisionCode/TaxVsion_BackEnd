using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Application.Catalog.Confirmation;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Migrations;

namespace TaxVision.Subscription.Application.Catalog.Migrations;

public sealed record PreviewSubscriberMigrationQuery(Guid PlanId, Guid ToVersionId, string Mode, int NoticeDays);

public sealed record StartSubscriberMigrationCommand(
    Guid PlanId,
    Guid ToVersionId,
    string Mode,
    int NoticeDays,
    string? Reason,
    string? PreviewToken,
    Guid ActorUserId
);

public sealed record CancelSubscriberMigrationCommand(Guid MigrationId, string? Reason, Guid ActorUserId);

public sealed record GetSubscriberMigrationQuery(Guid MigrationId);

public sealed record SubscriberMigrationOfficeResponse(
    Guid TenantId,
    string Outcome,
    string? Detail,
    DateTime? EffectiveAtUtc,
    DateTime? ProcessedAtUtc
);

public sealed record SubscriberMigrationResponse(
    Guid Id,
    Guid PlanId,
    string PlanCode,
    int ToEdition,
    string Mode,
    int NoticeDays,
    string Reason,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? CancelledAtUtc,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<SubscriberMigrationOfficeResponse> Offices
);

public static class PreviewSubscriberMigrationHandler
{
    public static Task<Result<SubscriberMigrationPreview>> Handle(
        PreviewSubscriberMigrationQuery query,
        SubscriberMigrationPlanner planner,
        CancellationToken ct
    ) => planner.PreviewAsync(query.PlanId, query.ToVersionId, query.Mode, query.NoticeDays, DateTime.UtcNow, ct);
}

/// <summary>
/// Confirma la vista previa: exige su token vigente y un motivo, y deja la migración corriendo. Las oficinas las mueve
/// el job, una por transacción. Queda auditada.
/// </summary>
public static class StartSubscriberMigrationHandler
{
    public static async Task<Result<Guid>> Handle(
        StartSubscriberMigrationCommand command,
        SubscriberMigrationPlanner planner,
        ISubscriberMigrationRepository migrations,
        IUnitOfWork unitOfWork,
        ICatalogAuditLog audit,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var request = CatalogConfirmation.ValidateRequest(command.PreviewToken, command.Reason);
        if (request.IsFailure)
            return Result.Failure<Guid>(request.Error);

        var nowUtc = DateTime.UtcNow;
        var preview = await planner.PreviewAsync(
            command.PlanId,
            command.ToVersionId,
            command.Mode,
            command.NoticeDays,
            nowUtc,
            ct
        );
        if (preview.IsFailure)
            return Result.Failure<Guid>(preview.Error);
        if (!CatalogConfirmation.Matches(preview.Value.PreviewToken, command.PreviewToken!))
            return Result.Failure<Guid>(CatalogConfirmation.Stale);

        var started = SubscriberMigration.Start(
            preview.Value.PlanId,
            preview.Value.PlanCode,
            preview.Value.ToVersionId,
            preview.Value.ToEdition,
            Enum.Parse<SubscriberMigrationMode>(preview.Value.Mode),
            preview.Value.NoticeDays,
            preview.Value.OnlyImprovement,
            command.Reason!,
            command.ActorUserId,
            preview
                .Value.Offices.Select(office => new SubscriberMigrationOfficePlan(
                    office.TenantId,
                    office.SubscriptionId,
                    office.BlockedReason
                ))
                .ToList(),
            nowUtc
        );
        if (started.IsFailure)
            return Result.Failure<Guid>(started.Error);

        var migration = started.Value;
        await migrations.AddAsync(migration, ct);
        await CatalogAudit.RecordAsync(
            audit,
            CatalogAuditActor.ForUser(command.ActorUserId),
            "subscriber_migration.started",
            new CatalogAuditTarget("SubscriberMigration", migration.Id, migration.PlanCode),
            before: null,
            new
            {
                migration.PlanCode,
                ToEdition = migration.ToVersionNumber,
                Mode = migration.Mode.ToString(),
                migration.NoticeDays,
                Offices = migration.Offices.Count,
                Blocked = migration.Offices.Count(office => office.Outcome == SubscriberMigrationOutcome.Blocked),
            },
            migration.Reason,
            correlation.CorrelationId,
            nowUtc,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(migration.Id);
    }
}

/// <summary>Cancela una migración "en la renovación": el job desagenda lo que todavía no se aplicó.</summary>
public static class CancelSubscriberMigrationHandler
{
    public static async Task<Result> Handle(
        CancelSubscriberMigrationCommand command,
        ISubscriberMigrationRepository migrations,
        IUnitOfWork unitOfWork,
        ICatalogAuditLog audit,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var migration = await migrations.GetForUpdateAsync(command.MigrationId, ct);
        if (migration is null)
            return Result.Failure(new Error("Migration.NotFound", "Migration not found."));

        var before = migration.Status.ToString();
        var nowUtc = DateTime.UtcNow;
        var cancelled = migration.Cancel(command.ActorUserId, command.Reason ?? string.Empty, nowUtc);
        if (cancelled.IsFailure)
            return cancelled;

        await CatalogAudit.RecordIfChangedAsync(
            audit,
            CatalogAuditActor.ForUser(command.ActorUserId),
            "subscriber_migration.cancelled",
            new CatalogAuditTarget("SubscriberMigration", migration.Id, migration.PlanCode),
            new { Status = before },
            new { Status = migration.Status.ToString() },
            command.Reason,
            correlation.CorrelationId,
            nowUtc,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public static class GetSubscriberMigrationHandler
{
    public static async Task<Result<SubscriberMigrationResponse>> Handle(
        GetSubscriberMigrationQuery query,
        ISubscriberMigrationRepository migrations,
        CancellationToken ct
    )
    {
        var migration = await migrations.GetAsync(query.MigrationId, ct);
        if (migration is null)
            return Result.Failure<SubscriberMigrationResponse>(new Error("Migration.NotFound", "Migration not found."));

        return Result.Success(
            new SubscriberMigrationResponse(
                migration.Id,
                migration.PlanId,
                migration.PlanCode,
                migration.ToVersionNumber,
                migration.Mode.ToString(),
                migration.NoticeDays,
                migration.Reason,
                migration.Status.ToString(),
                migration.CreatedAtUtc,
                migration.CompletedAtUtc,
                migration.CancelledAtUtc,
                migration
                    .Offices.GroupBy(office => office.Outcome.ToString())
                    .ToDictionary(group => group.Key, group => group.Count()),
                migration
                    .Offices.OrderBy(office => office.TenantId)
                    .Select(office => new SubscriberMigrationOfficeResponse(
                        office.TenantId,
                        office.Outcome.ToString(),
                        office.Detail,
                        office.EffectiveAtUtc,
                        office.ProcessedAtUtc
                    ))
                    .ToList()
            )
        );
    }
}
