using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Invitations;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Customers.IntegrationEvents;

public static class CustomerPortalInvitationRequestedConsumer
{
    private static readonly TimeSpan InvitationValidity = TimeSpan.FromDays(7);

    public static async Task Handle(
        CustomerPortalInvitationRequestedIntegrationEvent evt,
        IUserRepository users,
        ITenantRegistry tenants,
        IInvitationRepository invitations,
        IInvitationTokenService tokenService,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<CustomerPortalInvitationRequestedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            // 1) Tenant existe y está activo
            var tenant = await tenants.GetByIdAsync(evt.TenantId, ct);
            if (tenant is null || !tenant.IsActive)
            {
                logger.LogWarning(
                    "Skipping portal invitation: tenant {TenantId} not found or inactive (customer {CustomerId}).",
                    evt.TenantId,
                    evt.CustomerId
                );
                return;
            }

            var normalizedEmail = evt.Email.Trim().ToLowerInvariant();

            // 2) Idempotencia: el customer ya tiene un User activo con ese email
            if (await users.EmailExistsAsync(evt.TenantId, normalizedEmail, ct))
            {
                logger.LogInformation(
                    "Portal user already exists for {Email} in tenant {TenantId}; ignoring event.",
                    normalizedEmail,
                    evt.TenantId
                );
                return;
            }

            // 3) Idempotencia: ya hay invitación pendiente para ese email+tenant
            if (await invitations.HasPendingAsync(evt.TenantId, normalizedEmail, ct))
            {
                logger.LogInformation(
                    "Pending portal invitation already exists for {Email} in tenant {TenantId}; ignoring event.",
                    normalizedEmail,
                    evt.TenantId
                );
                return;
            }

            // 4) Crear la invitación CustomerPortal
            var token = tokenService.Generate();
            var expiresAtUtc = DateTime.UtcNow.Add(InvitationValidity);

            var invitationResult = Invitation.Create(
                tenantId: evt.TenantId,
                email: normalizedEmail,
                actorType: UserActorType.CustomerPortal,
                customerId: evt.CustomerId,
                invitedByUserId: evt.RequestedByUserId,
                tokenHash: token.TokenHash,
                expiresAtUtc: expiresAtUtc
            );

            if (invitationResult.IsFailure)
            {
                logger.LogError(
                    "Failed to create portal invitation for customer {CustomerId}: {Error}",
                    evt.CustomerId,
                    invitationResult.Error.Message
                );
                throw new InvalidOperationException(invitationResult.Error.Message);
            }

            await invitations.AddAsync(invitationResult.Value, ct);
            await unitOfWork.SaveChangesAsync(ct);

            // 5) TEMP DEV ONLY: loguear el token plain hasta que exista Email Service.
            // En producción Auth publicará CustomerPortalInvitationCreatedV1 con el token raw
            // para que Email Service lo consuma y envíe el correo.
            logger.LogInformation(
                "[DEV] Portal invitation created for customer {CustomerId} in tenant {TenantId}. "
                    + "Token (use to activate): {Token}",
                evt.CustomerId,
                evt.TenantId,
                token.RawToken
            );
        }
    }
}
