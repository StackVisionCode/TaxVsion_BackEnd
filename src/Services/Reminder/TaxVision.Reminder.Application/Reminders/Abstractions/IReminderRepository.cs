using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Reminder.Domain.Reminders;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Application.Reminders.Abstractions;

/// <summary>
/// Guardrail #5: los lookups que pueden fallar devuelven <see cref="Result{T}"/>, no <c>null</c>.
///
/// <para>
/// ⚠️ <b>Todos</b> los métodos que reciben un <c>tenantId</c> validado usan
/// <c>IgnoreQueryFilters()</c> + filtro explícito, no el filtro global. Medido en vivo (Fase 6): los
/// handlers corren dentro del scope de DI que crea <b>Wolverine</b> para cada <c>InvokeAsync</c>, no
/// en el de la request HTTP donde <c>JwtTenantContextMiddleware</c> pobló el tenant — el
/// <c>TenantContext</c> que ve el DbContext del handler está vacío y el filtro fail-closed devuelve
/// <b>0 filas</b>. Es el mismo root cause que ya se documentó en <c>LocalCommandTenantMiddleware</c>.
/// </para>
///
/// <para>
/// ⚠️ Los métodos marcados como <b>cross-tenant</b> corren fuera de un request HTTP (jobs), donde
/// no hay <c>TenantId</c> en contexto. El filtro global de EF es fail-closed: sin
/// <c>IgnoreQueryFilters()</c> devuelven <b>0 filas siempre</b> y el job parece sano mientras no
/// hace nada. Es el bug que ya se evitó en <c>CodeReservationRepository.GetActiveExpiredAsync</c>
/// de Growth.
/// </para>
/// </summary>
public interface IReminderRepository
{
    void Add(ReminderAggregate reminder);

    Task<Result<ReminderAggregate>> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// La lectura que usa <b>todo</b> comando y query de un solo recordatorio. El <c>userId</c> va
    /// en el predicado, no en un chequeo posterior: un recordatorio es estrictamente privado
    /// (invariante R1) y devolver <c>NotFound</c> —nunca <c>Forbidden</c>— es lo que impide filtrar
    /// la existencia de recordatorios ajenos.
    /// </summary>
    Task<Result<ReminderAggregate>> GetOwnedAsync(
        Guid tenantId,
        Guid userId,
        Guid reminderId,
        CancellationToken ct = default
    );

    /// <summary>Los recordatorios del usuario, opcionalmente filtrados por estado. Más recientes primero.</summary>
    Task<PagedResult<ReminderAggregate>> ListForUserAsync(
        Guid tenantId,
        Guid userId,
        ReminderStatus? status,
        int page,
        int size,
        CancellationToken ct = default
    );

    /// <summary>
    /// Agenda del usuario en un rango: solo <c>Scheduled</c> y <c>Snoozed</c> — lo que todavía va a
    /// sonar. Ordenados por <c>FireAtUtc</c> ascendente, que es como se pinta una agenda.
    /// </summary>
    Task<PagedResult<ReminderAggregate>> ListUpcomingForUserAsync(
        Guid tenantId,
        Guid userId,
        DateTime fromUtc,
        DateTime toUtc,
        int page,
        int size,
        CancellationToken ct = default
    );

    /// <summary>
    /// Soporte de la idempotencia de ADR-R-07: antes de crear, el handler pregunta si esta
    /// <c>RequestKey</c> ya existe. Devuelve <c>null</c> deliberadamente — «no existe» es el caso
    /// normal y esperado del alta, no un fallo.
    /// </summary>
    Task<ReminderAggregate?> FindByRequestKeyAsync(
        Guid tenantId,
        RequestKey requestKey,
        CancellationToken ct = default
    );

    /// <summary>
    /// Resuelve <c>reminder.target_moved.v1</c> / <c>target_closed.v1</c>: todos los recordatorios
    /// aún pendientes que apuntan a ese objetivo dentro del tenant del evento.
    /// </summary>
    Task<IReadOnlyList<ReminderAggregate>> ListPendingByTargetAsync(
        Guid tenantId,
        ReminderCategory category,
        Guid targetId,
        CancellationToken ct = default
    );

    /// <summary>
    /// <b>Cross-tenant.</b> Carga para el job de Quartz, que corre fuera de un request y por lo
    /// tanto sin <c>TenantId</c> en contexto: <see cref="GetByIdAsync"/> devolvería
    /// <c>NotFound</c> siempre. El tenant llega explícito desde el <c>JobDataMap</c> del trigger y
    /// se filtra a mano — saltarse el filtro global sin volver a filtrar por tenant sería abrir el
    /// servicio a leer recordatorios ajenos.
    /// </summary>
    Task<Result<ReminderAggregate>> GetForSchedulerAsync(
        Guid tenantId,
        Guid reminderId,
        CancellationToken ct = default
    );

    /// <summary>
    /// <b>Cross-tenant.</b> Red de seguridad de la Fase 5: recordatorios agendados que disparan
    /// dentro del horizonte y que podrían haber quedado sin trigger vivo en Quartz (EF y Quartz no
    /// comparten transacción).
    /// </summary>
    Task<IReadOnlyList<ReminderAggregate>> ListScheduledWithinHorizonAsync(
        DateTime horizonUtc,
        CancellationToken ct = default
    );
}
