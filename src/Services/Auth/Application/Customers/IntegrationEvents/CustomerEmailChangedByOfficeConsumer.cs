using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Credentials.Administration;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Customers.IntegrationEvents;

/// <summary>
/// D-08: la oficina cambió el correo de un cliente que tiene portal. Hasta hoy Auth no se enteraba (el login quedaba
/// con el correo viejo para siempre). Ahora pide la verificación del nuevo con el mismo flujo del cambio de correo
/// (link al nuevo, aviso al anterior); el login cambia solo cuando el cliente confirma. Un cambio hecho por el
/// propio cliente, un correo igual al de login o un pedido ya hecho para ese correo no hacen nada.
/// </summary>
public static class CustomerEmailChangedByOfficeConsumer
{
    public static async Task Handle(
        CustomerUpdatedIntegrationEvent evt,
        IUserRepository users,
        ICredentialTokenRepository credentials,
        ISecureTokenService tokens,
        IAccountTokenProtector tokenProtector,
        ITenantRegistry tenants,
        IOptions<CredentialAdministrationOptions> options,
        IAuthAuditWriter audit,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(evt.PrimaryEmail))
            return;
        // Solo si el correo cambió en esta edición; un evento viejo (sin PreviousEmail) sigue como antes.
        if (
            evt.PreviousEmail is not null
            && string.Equals(evt.PreviousEmail, evt.PrimaryEmail, StringComparison.OrdinalIgnoreCase)
        )
            return;
        using var _ = correlation.Push(
            string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
        );

        var user = await users.GetPortalUserByCustomerAsync(evt.TenantId, evt.CustomerId, ct);
        if (user is null || !user.IsActive || evt.ModifiedByUserId == user.Id)
            return;
        // Solo quien administra credenciales (hoy el Owner, D-01) mueve el correo de login; la edición de un
        // empleado, de una importación o de un formulario queda solo en la ficha (03 §21, cambio malicioso).
        var actor = await users.GetByIdAsync(evt.ModifiedByUserId, ct);
        if (
            actor is null
            || CredentialAdministrationPolicy
                .CanActOn(
                    actor,
                    user,
                    CredentialAdministrationAction.ChangeEmail,
                    UserAccountKind.Portal,
                    options.Value.OwnerOnly
                )
                .IsFailure
        )
            return;
        var newEmail = await OfficeEmailChange.ValidateAsync(user, evt.PrimaryEmail, users, ct);
        if (newEmail.IsFailure)
            return;
        // CustomerUpdated llega por cualquier cambio de la ficha: un pedido ya hecho para este correo no se repite.
        var latest = await credentials.GetLatestEmailVerificationAsync(user.Id, ct);
        if (latest is not null && string.Equals(latest.NewEmail, newEmail.Value, StringComparison.OrdinalIgnoreCase))
            return;

        var (_, requested) = await OfficeEmailChange.IssueAsync(
            user,
            newEmail.Value,
            evt.ModifiedByUserId,
            OfficeEmailChange.LinkLifetime(options.Value),
            credentials,
            tokens,
            tokenProtector,
            tenants,
            correlation.CorrelationId,
            ct
        );
        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                evt.ModifiedByUserId,
                AuthAuditAction.EmailChangeRequestedByOffice,
                true,
                null,
                null,
                correlation.CorrelationId,
                targetType: "User",
                targetId: user.Id,
                detailsJson: JsonSerializer.Serialize(
                    new
                    {
                        accountKind = "Portal",
                        modality = OfficeEmailChange.Modality,
                        customerId = evt.CustomerId,
                    }
                )
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(requested);
    }
}
