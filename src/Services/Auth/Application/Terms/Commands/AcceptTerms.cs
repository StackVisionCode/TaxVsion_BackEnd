using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Onboarding.TermsVersions;
using TaxVision.Auth.Domain.Terms;
using Wolverine;

namespace TaxVision.Auth.Application.Terms.Commands;

/// <summary>
/// Fase L1.4 — registra que el tenant acepto la version vigente del ToS/AUP. Siempre exitoso e
/// idempotente en efecto: si ya existe una fila para (tenant, usuario, TermsVersion actual) se
/// devuelve esa misma sin insertar de nuevo — nunca falla por "ya aceptado".
///
/// PayFlow Fase 6 (retrofit): la version vigente se resuelve ahora contra la tabla
/// Onboarding.TermsVersions (Kind=TermsOfService, Locale="en-US" — el flujo self-service no
/// soporta locale todavia). La migracion de retrofit garantiza que esa fila siempre existe (el
/// seed legacy si nadie publico una version real todavia), asi que la ausencia total se trata
/// como una violacion de invariante, no un resultado de negocio esperable.
/// </summary>
public sealed record AcceptTermsCommand(Guid TenantId, Guid UserId);

public sealed record TermsAcceptanceResponse(string TermsVersion, DateTime AcceptedAtUtc);

public static class AcceptTermsHandler
{
    private const string DefaultLocale = "en-US";

    public static async Task<TermsAcceptanceResponse> Handle(
        AcceptTermsCommand command,
        ITenantTermsAcceptanceRepository acceptances,
        ITermsVersionRepository termsVersions,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;
        var currentVersion =
            await termsVersions.GetCurrentAsync(TermsKind.TermsOfService, DefaultLocale, nowUtc, ct)
            ?? throw new InvalidOperationException(
                "No TermsVersion is published for TermsOfService/en-US — the Fase 6 retrofit migration should have seeded a legacy row."
            );

        var existing = await acceptances.GetByVersionAsync(command.TenantId, command.UserId, currentVersion.Id, ct);
        if (existing is not null)
            return new TermsAcceptanceResponse(existing.TermsVersion, existing.AcceptedAtUtc);

        var acceptance = TenantTermsAcceptance.Accept(
            command.TenantId,
            command.UserId,
            currentVersion.Version,
            currentVersion.Id,
            contentHash: null,
            acceptedInContext: "ReAcceptance",
            request.IpAddress,
            request.UserAgent,
            nowUtc
        );
        await acceptances.AddAsync(acceptance, ct);

        await audit.AddAsync(
            AuthAuditLog.Record(
                command.TenantId,
                command.UserId,
                AuthAuditAction.TermsAccepted,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "TenantTermsAcceptance",
                targetId: acceptance.Id,
                detailsJson: $$"""{"version":"{{currentVersion.Version}}"}"""
            ),
            ct
        );

        await bus.PublishAsync(
            new TenantTermsAcceptedIntegrationEvent
            {
                TenantId = command.TenantId,
                AcceptedByUserId = command.UserId,
                TermsVersion = currentVersion.Version,
                CorrelationId = correlation.CorrelationId,
            }
        );

        await unitOfWork.SaveChangesAsync(ct);
        return new TermsAcceptanceResponse(currentVersion.Version, nowUtc);
    }
}
