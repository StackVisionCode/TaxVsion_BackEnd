using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.Admin.Commands;
using TaxVision.Auth.Domain.Audit;
using Wolverine;

namespace TaxVision.Auth.Application.Onboarding.TenantOnboardings.Commands;

/// <summary>OBF-2.5 — resend end-user del email "completa tu oficina" disparado por el botón "Resend" del
/// fallback del landing. Autentica por reference (misma primitive del resume), rate-limita a nivel endpoint,
/// y aplica cooldown de 60s sobre el aggregate para no spamear al comprador si clickean varias veces.</summary>
public sealed record ResendRegistrationEmailPublicCommand(Guid OnboardingId);

public static class ResendRegistrationEmailPublicHandler
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);

    public static async Task<Result> Handle(
        ResendRegistrationEmailPublicCommand command,
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
        var onboarding = await onboardings.GetByIdAsync(command.OnboardingId, ct);
        if (onboarding is null)
            return Result.Failure(new Error("Onboarding.NotFound", "Onboarding not found."));

        var nowUtc = DateTime.UtcNow;
        // Cooldown corto: si el último email salió hace menos de 60s, rechazamos — así el botón "Resend"
        // no spamea al comprador si clickea varias veces. El rate limit del endpoint ya cubre abuso por
        // reference comprometido; esto cubre doble-click honesto.
        if (onboarding.RegistrationEmailSentAtUtc is { } lastSent && nowUtc - lastSent < Cooldown)
            return Result.Failure(
                new Error(
                    "Onboarding.ResendTooSoon",
                    "A registration email was already sent recently. Please wait before requesting another."
                )
            );

        var regenerate = await ResendOnboardingRegistrationEmailAdminHandler.RegenerateAndPublishAsync(
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
                userId: Guid.Empty,
                AuthAuditAction.OnboardingSelfServiceRegistrationEmailResent,
                success: true,
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
}
