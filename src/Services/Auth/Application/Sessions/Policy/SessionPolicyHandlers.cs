using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Sessions;

namespace TaxVision.Auth.Application.Sessions.Policy;

/// <summary>La política vigente (la que leen el CRM, el portal y PlatformAdmin).</summary>
public sealed record GetSessionPolicyQuery;

/// <summary>PlatformAdmin cambia la política de sesión de toda la plataforma. Exige motivo.</summary>
public sealed record UpdateSessionPolicyCommand(
    int IdleTimeoutMinutes,
    int WarningSeconds,
    int AbsoluteLifetimeHours,
    string Reason,
    Guid ActorUserId
);

public sealed record SessionPolicyView(
    int IdleTimeoutMinutes,
    int WarningSeconds,
    int AbsoluteLifetimeHours,
    DateTime UpdatedAtUtc
)
{
    public static SessionPolicyView From(PlatformSessionPolicy policy) =>
        new(policy.IdleTimeoutMinutes, policy.WarningSeconds, policy.AbsoluteLifetimeHours, policy.UpdatedAtUtc);
}

public static class GetSessionPolicyHandler
{
    public static async Task<Result<SessionPolicyView>> Handle(
        GetSessionPolicyQuery query,
        ISessionPolicyRepository policies,
        CancellationToken ct
    )
    {
        var policy = await policies.GetAsync(ct) ?? PlatformSessionPolicy.CreateDefault(DateTime.UtcNow);
        return Result.Success(SessionPolicyView.From(policy));
    }
}

/// <summary>
/// Valida con el dominio, guarda la política y su auditoría (antes y después, con el motivo) en la misma
/// transacción, y recién después descarta la caché: el siguiente refresh ya aplica los valores nuevos.
/// </summary>
public static class UpdateSessionPolicyHandler
{
    // El motivo se guarda legible (con tildes), no escapado.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static async Task<Result<SessionPolicyView>> Handle(
        UpdateSessionPolicyCommand command,
        ISessionPolicyRepository policies,
        ISessionLifetimeGate gate,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;
        var policy = await policies.GetAsync(ct);
        var isNew = policy is null;
        policy ??= PlatformSessionPolicy.CreateDefault(nowUtc);
        var before = SessionPolicyView.From(policy);

        var changed = policy.Change(
            command.IdleTimeoutMinutes,
            command.WarningSeconds,
            command.AbsoluteLifetimeHours,
            command.Reason,
            command.ActorUserId,
            nowUtc
        );
        if (changed.IsFailure)
            return Result.Failure<SessionPolicyView>(changed.Error);

        if (isNew)
            await policies.AddAsync(policy, ct);

        var after = SessionPolicyView.From(policy);
        await audit.AddAsync(
            AuthAuditLog.Record(
                Guid.Empty,
                command.ActorUserId,
                AuthAuditAction.SessionPolicyUpdated,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: nameof(PlatformSessionPolicy),
                detailsJson: JsonSerializer.Serialize(
                    new
                    {
                        reason = command.Reason.Trim(),
                        before = new
                        {
                            before.IdleTimeoutMinutes,
                            before.WarningSeconds,
                            before.AbsoluteLifetimeHours,
                        },
                        after = new
                        {
                            after.IdleTimeoutMinutes,
                            after.WarningSeconds,
                            after.AbsoluteLifetimeHours,
                        },
                    },
                    Json
                )
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        gate.Invalidate();
        return Result.Success(after);
    }
}
