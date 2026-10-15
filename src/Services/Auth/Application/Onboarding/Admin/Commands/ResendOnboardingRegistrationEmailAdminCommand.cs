using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Platform;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Onboarding.ValueObjects;
using Wolverine;

namespace TaxVision.Auth.Application.Onboarding.Admin.Commands;

public sealed record ResendOnboardingRegistrationEmailAdminCommand(Guid OnboardingId, string Reason, Guid AdminUserId);

/// <summary>OBF-3.1 — admin resend del email "completa tu oficina" cuando el cliente pagó pero el link
/// original se perdió (token Redis vencido, scheduler quemó el intento, cliente borró el correo). Reemite
/// un token nuevo de 7 días y publica el evento al mismo consumer que el flujo normal. Reusable para los
/// casos RegistrationPending (cliente activo) y PaymentCompleted (nunca llegó al email). Para rescates
/// desde Expired usar ReactivateExpiredOnboardingAdminCommand.</summary>
public static class ResendOnboardingRegistrationEmailAdminHandler
{
    private static readonly TimeSpan NewTokenLifetime = TimeSpan.FromDays(7);

    public static async Task<Result> Handle(
        ResendOnboardingRegistrationEmailAdminCommand command,
        ITenantOnboardingRepository onboardings,
        ISecureTokenService tokens,
        ITokenReferenceStore tokenReferences,
        IPlanCatalogClient planCatalog,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        IOptions<OnboardingOptions> onboardingOptions,
        IAuthAuditWriter audit,
        IRequestContext request,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
            return Result.Failure(new Error("OnboardingAdmin.ReasonRequired", "A reason is required."));

        var onboarding = await onboardings.GetByIdAsync(command.OnboardingId, ct);
        if (onboarding is null)
            return Result.Failure(new Error("Onboarding.NotFound", "Onboarding not found."));

        var regenerate = await RegenerateAndPublishAsync(
            onboarding,
            tokens,
            tokenReferences,
            planCatalog,
            bus,
            correlation,
            onboardingOptions.Value,
            ct
        );
        if (regenerate.IsFailure)
            return regenerate;

        await audit.AddAsync(
            AuthAuditLog.Record(
                onboarding.TenantId ?? Guid.Empty,
                command.AdminUserId,
                AuthAuditAction.OnboardingAdminRegistrationEmailResent,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "TenantOnboarding",
                targetId: onboarding.Id
            ),
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Compartido con ReactivateExpiredOnboardingAdminHandler: emite nuevo token (Redis + hash +
    /// expiración), reinicia CreatedAtUtc para que el sweeper de expiración no lo tumbe, y publica el evento
    /// del email con el token raw dentro. Marca EmailSent dentro de la misma transacción outbox.</summary>
    internal static async Task<Result> RegenerateAndPublishAsync(
        Domain.Onboarding.TenantOnboardings.TenantOnboarding onboarding,
        ISecureTokenService tokens,
        ITokenReferenceStore tokenReferences,
        IPlanCatalogClient planCatalog,
        IMessageBus bus,
        ICorrelationContext correlation,
        OnboardingOptions options,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;
        var rawToken = tokens.GenerateToken();
        var hashResult = RegistrationTokenHash.Create(tokens.Hash(rawToken));
        if (hashResult.IsFailure)
            return Result.Failure(hashResult.Error);

        var tokenReference = Guid.NewGuid();
        var regenerate = onboarding.AdminRegenerateRegistrationToken(
            hashResult.Value,
            nowUtc.Add(NewTokenLifetime),
            tokenReference,
            nowUtc
        );
        if (regenerate.IsFailure)
            return regenerate;

        // Raw token en Redis el tiempo necesario para que el consumer de email arme el link; el hash en
        // la BD dura NewTokenLifetime porque el usuario puede clickear mucho después.
        await tokenReferences.StoreAsync(tokenReference, rawToken, ct);

        var planName = await planCatalog.GetPlanNameAsync(onboarding.PlanId, ct);
        bus.TenantId = PlatformTenant.Id.ToString();
        await bus.PublishAsync(
            new OnboardingRegistrationReadyIntegrationEvent
            {
                TenantId = PlatformTenant.Id,
                OnboardingId = onboarding.Id,
                TokenReference = tokenReference,
                Email = onboarding.Email,
                FirstName = onboarding.FirstName,
                PlanName = planName,
                PriceFormatted = FormatPrice(onboarding.NetAmountCents, onboarding.Currency),
                PaidAtUtc = onboarding.PaymentCompletedAtUtc ?? nowUtc,
                RegistrationUrlBase = options.RegistrationUrlBase,
                CorrelationId = correlation.CorrelationId,
            }
        );
        onboarding.MarkRegistrationEmailSent(nowUtc);
        return Result.Success();
    }

    private static string FormatPrice(long? amountCents, string? currency) =>
        amountCents is null || currency is null
            ? string.Empty
            : $"{(amountCents.Value / 100m).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} {currency}";
}
