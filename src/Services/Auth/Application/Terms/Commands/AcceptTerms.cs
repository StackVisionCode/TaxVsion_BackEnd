using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Terms;
using Wolverine;

namespace TaxVision.Auth.Application.Terms.Commands;

/// <summary>Fase L1.4 — registra que el tenant acepto la version vigente del ToS/AUP. Siempre exitoso e idempotente en efecto: cada llamada agrega una fila nueva, nunca falla por "ya aceptado".</summary>
public sealed record AcceptTermsCommand(Guid TenantId, Guid UserId);

public sealed record TermsAcceptanceResponse(string TermsVersion, DateTime AcceptedAtUtc);

public static class AcceptTermsHandler
{
    public static async Task<TermsAcceptanceResponse> Handle(
        AcceptTermsCommand command,
        ITenantTermsAcceptanceRepository acceptances,
        IOptions<TermsOptions> options,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var version = options.Value.CurrentVersion;
        var nowUtc = DateTime.UtcNow;
        var acceptance = TenantTermsAcceptance.Accept(
            command.TenantId,
            command.UserId,
            version,
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
                detailsJson: $$"""{"version":"{{version}}"}"""
            ),
            ct
        );

        await bus.PublishAsync(
            new TenantTermsAcceptedIntegrationEvent
            {
                TenantId = command.TenantId,
                AcceptedByUserId = command.UserId,
                TermsVersion = version,
                CorrelationId = correlation.CorrelationId,
            }
        );

        await unitOfWork.SaveChangesAsync(ct);
        return new TermsAcceptanceResponse(version, nowUtc);
    }
}
