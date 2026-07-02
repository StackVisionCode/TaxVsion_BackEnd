using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Invitations;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Customers.IntegrationEvents;

/// <summary>
/// El servicio Customer solicita invitar a un cliente al portal. Auth crea la
/// invitación CustomerPortal y publica InvitationCreated para que Notification
/// envíe el email. El token nunca se registra en logs.
/// </summary>
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
        IMessageBus bus,
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

            invitationResult.Value.MarkSent();
            await invitations.AddAsync(invitationResult.Value, ct);

            // 5) Notification entrega el email con el enlace de activación.
            //    El token viaja solo por el bus interno (outbox durable).
            await bus.PublishAsync(new InvitationCreatedIntegrationEvent
            {
                TenantId = evt.TenantId,
                InvitationId = invitationResult.Value.Id,
                Email = normalizedEmail,
                ActorType = UserActorType.CustomerPortal.ToString(),
                RawToken = token.RawToken,
                ExpiresAtUtc = expiresAtUtc,
                TenantName = tenant.Name,
                InviterName = evt.DisplayName,
                CorrelationId = correlationId
            });

            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation(
                "Portal invitation {InvitationId} created for customer {CustomerId} in tenant {TenantId}.",
                invitationResult.Value.Id,
                evt.CustomerId,
                evt.TenantId
            );
        }
    }
}
