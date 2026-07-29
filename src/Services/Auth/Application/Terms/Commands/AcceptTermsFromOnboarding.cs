using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Terms;
using Wolverine;

namespace TaxVision.Auth.Application.Terms.Commands;

/// <summary>
/// PayFlow Fase 6 — registra la aceptacion de terminos capturada durante el checkout de
/// onboarding (UoW #8 de la Saga, Fase 15). A diferencia de AcceptTermsCommand (self-service, la
/// version se resuelve del lado del servidor), aca el llamador ya conoce el TermsVersionId y su
/// ContentHash exacto porque el frontend los mostro al usuario en el momento de la aceptacion —
/// no hay ambiguedad de "cual es la version actual" que resolver.
/// </summary>
public sealed record AcceptTermsFromOnboardingCommand(
    Guid TenantId,
    Guid UserId,
    Guid TermsVersionId,
    string ContentHash,
    string? AcceptedFromIp,
    string? UserAgent
);

public static class AcceptTermsFromOnboardingHandler
{
    public static async Task<Result<TermsAcceptanceResponse>> Handle(
        AcceptTermsFromOnboardingCommand command,
        ITenantTermsAcceptanceRepository acceptances,
        ITermsVersionRepository termsVersions,
        IAuthAuditWriter audit,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var version = await termsVersions.GetByIdAsync(command.TermsVersionId, ct);
        if (version is null)
            return Result.Failure<TermsAcceptanceResponse>(
                new Error("TermsVersion.NotFound", "The requested terms version does not exist.")
            );

        var existing = await acceptances.GetByVersionAsync(command.TenantId, command.UserId, version.Id, ct);
        if (existing is not null)
            return Result.Success(new TermsAcceptanceResponse(existing.TermsVersion, existing.AcceptedAtUtc));

        var nowUtc = DateTime.UtcNow;
        var acceptance = TenantTermsAcceptance.Accept(
            command.TenantId,
            command.UserId,
            version.Version,
            version.Id,
            command.ContentHash,
            acceptedInContext: "Onboarding",
            command.AcceptedFromIp,
            command.UserAgent,
            nowUtc
        );
        await acceptances.AddAsync(acceptance, ct);

        await audit.AddAsync(
            AuthAuditLog.Record(
                command.TenantId,
                command.UserId,
                AuthAuditAction.TermsAccepted,
                true,
                command.AcceptedFromIp,
                command.UserAgent,
                correlation.CorrelationId,
                targetType: "TenantTermsAcceptance",
                targetId: acceptance.Id,
                detailsJson: $$"""{"version":"{{version.Version}}","context":"Onboarding"}"""
            ),
            ct
        );

        await bus.PublishAsync(
            new TenantTermsAcceptedIntegrationEvent
            {
                TenantId = command.TenantId,
                AcceptedByUserId = command.UserId,
                TermsVersion = version.Version,
                CorrelationId = correlation.CorrelationId,
            }
        );

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new TermsAcceptanceResponse(version.Version, nowUtc));
    }
}
