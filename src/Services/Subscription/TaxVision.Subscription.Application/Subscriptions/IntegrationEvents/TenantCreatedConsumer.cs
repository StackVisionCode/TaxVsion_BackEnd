using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Settings;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

/// <summary>
/// Alta de tenant ⇒ se crea la suscripción base en trial con el plan por defecto y su
/// configuración de políticas por defecto. RecalculateEntitlementsCommand publica
/// TenantEntitlementsChangedIntegrationEvent, que es lo que Auth/CloudStorage/etc.
/// consumen para proyectar los límites — no hay un evento de activación aparte.
///
/// La creación de la suscripción es idempotente (si ya existe, no se vuelve a crear), pero
/// el recalculo de entitlements se dispara SIEMPRE — RecalculateEntitlementsCommand es un
/// upsert, así que reprocesar este evento (redelivery, o cualquier reintento manual) también
/// sirve para autosanar un tenant que quedó con suscripción pero sin snapshot de entitlements
/// por una falla anterior. Usa RecalculateEntitlementsOrThrowAsync (no la variante "Safely") a
/// propósito: un fallo acá debe reintentarse (RetryWithCooldown de Program.cs) y terminar en la
/// dead-letter queue si persiste — no perderse en un simple log, que fue exactamente el bug real
/// de producción que dejó tenants sin TenantStorageLimits para siempre (ver
/// RecalculateEntitlementsExtensions.RecalculateEntitlementsOrThrowAsync).
/// </summary>
public static class TenantCreatedConsumer
{
    public static async Task Handle(
        TenantCreatedIntegrationEvent evt,
        ISubscriptionRepository subscriptions,
        ISubscriptionTenantSettingsRepository settingsRepository,
        IPlanRepository plans,
        IOptions<SubscriptionOptions> options,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TenantSubscription> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            if (IsPlatformTenant(evt.Kind))
                return;

            // PayFlow (Fase 16) — un tenant creado vía onboarding pago-primero activa su
            // suscripción directo en Active vía subscriptions/internal/activate-from-onboarding
            // (ActivateFromOnboardingHandler), no con el trial automático de este consumer.
            if (evt.OnboardingId is not null)
                return;

            // El envelope TenantCreated llega sin tenant ambiental (*DEFAULT*): lo publica el
            // servicio Tenant desde el flujo de registro anónimo, sin bus.TenantId. Sellar el tenant
            // aquí (mismo patrón que los jobs de scheduling) estampa el envelope del recalc para que
            // LocalCommandTenantMiddleware lo restaure y su lookup no pegue el filtro fail-closed.
            bus.TenantId = evt.NewTenantId.ToString();

            if (await subscriptions.GetByTenantIdAsync(evt.NewTenantId, ct) is not null)
            {
                logger.LogInformation(
                    "Subscription already exists for tenant {TenantId}; skipping creation but "
                        + "still recalculating entitlements in case a previous attempt left it stale.",
                    evt.NewTenantId
                );
                await ScheduleEntitlementsRecalcAsync(bus, evt.NewTenantId);
                return;
            }

            var (plan, planVersion) = await LoadDefaultPlanOrThrow(
                plans,
                options.Value.DefaultPlanCode,
                evt.NewTenantId,
                logger,
                ct
            );

            var subscription = CreateTrialSubscriptionOrThrow(
                evt.NewTenantId,
                plan,
                planVersion,
                options.Value.TrialDays
            );
            await subscriptions.AddAsync(subscription, ct);

            await EnsureDefaultSettingsAsync(evt.NewTenantId, settingsRepository, ct);
            await unitOfWork.SaveChangesAsync(ct);

            await ScheduleEntitlementsRecalcAsync(bus, evt.NewTenantId);

            logger.LogInformation(
                "Trial subscription created for tenant {TenantId} on plan {PlanCode} until {TrialEnd}.",
                evt.NewTenantId,
                plan.Code.Value,
                subscription.TrialEndsAtUtc
            );
        }
    }

    /// <summary>
    /// Despacha el recálculo de entitlements como mensaje POST-COMMIT en vez de invocarlo en proceso
    /// dentro de la transacción del handler. Con AutoApplyTransactions + UseDurableOutboxOnAllSendingEndpoints,
    /// Wolverine retiene el mensaje publicado hasta que la transacción commitea; así el
    /// RecalculateEntitlementsCommand corre en su propia transacción/scope con la suscripción ya
    /// visible en la DB. Antes se usaba bus.InvokeAsync (síncrono, misma transacción): la suscripción
    /// recién creada y aún sin commitear NO era visible en la conexión separada del recalc →
    /// Subscription.NotFound → throw → rollback → el tenant quedaba SIN suscripción (la regresión que
    /// introdujo el cambio a OrThrow). El recalc es idempotente (upsert) y tiene su propio
    /// RetryWithCooldown + dead-letter, así que un fallo transitorio se auto-sana en el reintento.
    /// bus.TenantId (sellado arriba) estampa el tenant en el envelope del mensaje.
    /// </summary>
    private static async Task ScheduleEntitlementsRecalcAsync(IMessageBus bus, Guid tenantId) =>
        await bus.PublishAsync(new RecalculateEntitlementsCommand(tenantId));

    private static bool IsPlatformTenant(string kind) =>
        Enum.TryParse<TenantKind>(kind, ignoreCase: true, out var parsed) && parsed == TenantKind.Platform;

    private static async Task<(SubscriptionPlan Plan, SubscriptionPlanVersion Version)> LoadDefaultPlanOrThrow(
        IPlanRepository plans,
        string defaultPlanCode,
        Guid tenantId,
        ILogger logger,
        CancellationToken ct
    )
    {
        var plan = await plans.GetByCodeAsync(defaultPlanCode, ct);
        var version = plan?.GetPublishedVersion();
        if (plan is null || version is null)
        {
            logger.LogError(
                "Default plan '{PlanCode}' has no published version; cannot create subscription for tenant {TenantId}.",
                defaultPlanCode,
                tenantId
            );
            throw new InvalidOperationException($"Default plan '{defaultPlanCode}' is missing or unpublished.");
        }

        return (plan, version);
    }

    private static TenantSubscription CreateTrialSubscriptionOrThrow(
        Guid tenantId,
        SubscriptionPlan plan,
        SubscriptionPlanVersion planVersion,
        int trialDays
    )
    {
        var result = TenantSubscription.StartTrial(
            tenantId,
            plan,
            planVersion,
            trialDays,
            actorUserId: Guid.Empty,
            DateTime.UtcNow
        );
        if (result.IsFailure)
            throw new InvalidOperationException(result.Error.Message);

        return result.Value;
    }

    private static async Task EnsureDefaultSettingsAsync(
        Guid tenantId,
        ISubscriptionTenantSettingsRepository settingsRepository,
        CancellationToken ct
    )
    {
        if (await settingsRepository.GetByTenantIdAsync(tenantId, ct) is not null)
            return;

        var settingsResult = SubscriptionTenantSettings.Default(tenantId, actorUserId: Guid.Empty, DateTime.UtcNow);
        if (settingsResult.IsFailure)
            throw new InvalidOperationException(settingsResult.Error.Message);

        await settingsRepository.AddAsync(settingsResult.Value, ct);
    }
}
