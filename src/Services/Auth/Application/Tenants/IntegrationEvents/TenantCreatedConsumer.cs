using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Common;
using TaxVision.Auth.Application.Abstractions;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Domain.Invitations;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Tenants.IntegrationEvents;

public static class TenantCreatedConsumer
{
    public static async Task Handle(
        TenantCreatedIntegrationEvent evt,
        ITenantRegistry tenants,
        IInvitationRepository invitations,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var kind = Enum.TryParse<TenantKind>(evt.Kind, true, out var parsedKind)
                ? parsedKind
                : TenantKind.Customer;

            await tenants.UpsertCreatedAsync(
                evt.NewTenantId,
                evt.Name,
                evt.SubDomain,
                kind,
                evt.DefaultTimeZoneId,
                ct);

            if (kind == TenantKind.Customer)
            {
                var existing = await invitations.GetByTokenHashAsync(
                    evt.AdminInvitationTokenHash,
                    ct);
                if (existing is null)
                {
                    var expiresAtUtc =
                        evt.AdminInvitationExpiresAtUtc ?? DateTime.UtcNow.AddDays(7);

                    if (expiresAtUtc <= DateTime.UtcNow)
                    {
                        await unitOfWork.SaveChangesAsync(ct);
                        return;
                    }

                    var invitationResult = Invitation.Create(
                        evt.NewTenantId,
                        evt.AdminEmail,
                        UserActorType.TenantAdmin,
                        customerId: null,
                        invitedByUserId: null,
                        tokenHash: evt.AdminInvitationTokenHash,
                        expiresAtUtc: expiresAtUtc);
                    if (invitationResult.IsFailure)
                        throw new InvalidOperationException(invitationResult.Error.Message);

                    await invitations.AddAsync(invitationResult.Value, ct);
                }
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
