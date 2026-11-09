using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>
/// M2M: direcciones de todos los buzones del tenant (oficina y personales, en cualquier estado). Lo usa Correspondence
/// en la búsqueda (excluir los buzones propios de "personas", resolver el filtro <c>from</c> de lo enviado) y para llenar su proyección de dueños.
/// </summary>
public sealed record ListMailboxAddressesQuery(Guid TenantId);

public sealed record MailboxAddressDto(
    Guid AccountId,
    string EmailAddress,
    string? DisplayName,
    Guid? OwnerUserId = null,
    bool IsActive = false
);

public static class ListMailboxAddressesHandler
{
    public static async Task<IReadOnlyList<MailboxAddressDto>> Handle(
        ListMailboxAddressesQuery query,
        ITenantEmailAccountRepository accountRepository,
        CancellationToken ct
    )
    {
        var accounts = await accountRepository.ListByTenantAsync(query.TenantId, ct);
        return accounts
            .Select(a => new MailboxAddressDto(
                a.Id,
                a.EmailAddress,
                a.DisplayName,
                a.OwnerUserId,
                a.Status != TenantEmailAccountStatus.Disconnected
            ))
            .ToList();
    }
}
