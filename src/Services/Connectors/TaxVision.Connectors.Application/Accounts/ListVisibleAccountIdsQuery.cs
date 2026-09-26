namespace TaxVision.Connectors.Application.Accounts;

/// <summary>M2M: los AccountIds que un usuario puede ver — sus personales + la oficina si <paramref name="IncludeOffice"/>. Lo usa Correspondence para ocultar el correo del buzón de oficina a quien no tiene office.read.</summary>
public sealed record ListVisibleAccountIdsQuery(Guid TenantId, Guid UserId, bool IncludeOffice);

public static class ListVisibleAccountIdsHandler
{
    public static async Task<IReadOnlyList<Guid>> Handle(
        ListVisibleAccountIdsQuery query,
        ITenantEmailAccountRepository accountRepository,
        CancellationToken ct
    )
    {
        var accounts = await accountRepository.ListVisibleAsync(query.TenantId, query.UserId, query.IncludeOffice, ct);
        return accounts.Select(a => a.Id).ToList();
    }
}
