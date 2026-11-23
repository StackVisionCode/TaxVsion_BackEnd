using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Auth.Domain.Sessions;

/// <summary>
/// Cuánto puede durar una sesión web (CRM, portal y Account) en toda la plataforma. La cambia PlatformAdmin.
/// <para>
/// Es un agregado propio de Auth y no parte de <see cref="UserSession"/>: Auth es quien decide si una sesión
/// sigue valiendo, así que la regla vive donde está la invariante. Cambia por otra razón (decisión de
/// PlatformAdmin), en otro momento y sin tocar ninguna sesión: por eso no comparte transacción con ellas, solo
/// se lee al renovar. Hay una sola fila (<see cref="GlobalId"/>); el historial va en la auditoría de Auth.
/// </para>
/// </summary>
public sealed class PlatformSessionPolicy
{
    public const string GlobalId = "global";

    public const int MinIdleMinutes = 5;
    public const int MaxIdleMinutes = 240;
    public const int MinWarningSeconds = 30;
    public const int MaxWarningSeconds = 300;
    public const int MinAbsoluteHours = 1;
    public const int MaxAbsoluteHours = 72;

    private PlatformSessionPolicy() { }

    public string Id { get; private set; } = GlobalId;

    /// <summary>Minutos sin interacción antes del aviso.</summary>
    public int IdleTimeoutMinutes { get; private set; }

    /// <summary>Segundos que dura el aviso con cuenta atrás antes de cerrar.</summary>
    public int WarningSeconds { get; private set; }

    /// <summary>Máximo de una sesión aunque haya actividad.</summary>
    public int AbsoluteLifetimeHours { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid UpdatedBy { get; private set; }

    /// <summary>Concurrencia optimista: si dos admins guardan a la vez, el segundo recibe un conflicto.</summary>
    public byte[] Version { get; private set; } = [];

    /// <summary>30 min de inactividad (datos fiscales y personales: 15-30 es lo usual), 60 s de aviso, 12 h máximo.</summary>
    public static PlatformSessionPolicy CreateDefault(DateTime nowUtc) =>
        new()
        {
            IdleTimeoutMinutes = 30,
            WarningSeconds = 60,
            AbsoluteLifetimeHours = 12,
            UpdatedAtUtc = nowUtc,
            UpdatedBy = Guid.Empty,
        };

    public Result Change(
        int idleTimeoutMinutes,
        int warningSeconds,
        int absoluteLifetimeHours,
        string reason,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (
            idleTimeoutMinutes is < MinIdleMinutes or > MaxIdleMinutes
            || warningSeconds is < MinWarningSeconds or > MaxWarningSeconds
            || absoluteLifetimeHours is < MinAbsoluteHours or > MaxAbsoluteHours
        )
            return Result.Failure(
                new Error(
                    "SessionPolicy.OutOfRange",
                    $"Idle {MinIdleMinutes}-{MaxIdleMinutes} min, warning {MinWarningSeconds}-{MaxWarningSeconds} s, "
                        + $"absolute {MinAbsoluteHours}-{MaxAbsoluteHours} h."
                )
            );

        // El aviso es la parte final de la inactividad: tiene que ser más corto.
        if (warningSeconds >= idleTimeoutMinutes * 60)
            return Result.Failure(
                new Error("SessionPolicy.WarningTooLong", "The warning must be shorter than the idle time.")
            );

        // Con un máximo menor que la inactividad, la sesión se cortaría sin llegar a avisar.
        if (absoluteLifetimeHours * 60 < idleTimeoutMinutes)
            return Result.Failure(
                new Error(
                    "SessionPolicy.AbsoluteShorterThanIdle",
                    "The absolute lifetime cannot be shorter than the idle time."
                )
            );

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("SessionPolicy.ReasonRequired", "A reason is required."));

        IdleTimeoutMinutes = idleTimeoutMinutes;
        WarningSeconds = warningSeconds;
        AbsoluteLifetimeHours = absoluteLifetimeHours;
        UpdatedAtUtc = nowUtc;
        UpdatedBy = actorUserId;
        return Result.Success();
    }

    /// <summary>
    /// Por qué hay que cerrar esta sesión, o null si sigue valiendo. Solo gobierna sesiones web: la app móvil tiene
    /// su propio ciclo de vida.
    /// <para>
    /// <c>LastSeenAtUtc</c> se actualiza en cada renovación, y el front solo renueva mientras hay actividad. Por eso
    /// la inactividad se mide desde la última renovación con un margen de una vida de token (<paramref name="refreshCadence"/>):
    /// una sesión activa nunca pasa ese tiempo sin renovar. Es la defensa del servidor aunque el navegador falle.
    /// </para>
    /// </summary>
    public string? EndReason(UserSession session, TimeSpan refreshCadence, DateTime nowUtc)
    {
        if (session.Channel != SessionChannel.Web)
            return null;

        if (nowUtc - session.CreatedAtUtc >= TimeSpan.FromHours(AbsoluteLifetimeHours))
            return "absolute_lifetime";

        var idleLimit =
            TimeSpan.FromMinutes(IdleTimeoutMinutes) + TimeSpan.FromSeconds(WarningSeconds) + refreshCadence;
        return nowUtc - session.LastSeenAtUtc > idleLimit ? "idle_timeout" : null;
    }
}
