using TaxVision.Postmaster.Application.Abstractions;

namespace TaxVision.Postmaster.Application.Providers.Queries.GetProviderStatus;

public static class GetProviderStatusHandler
{
    public static async Task<ProviderStatusDto> Handle(
        GetProviderStatusQuery query,
        ISystemEmailProviderRepository systemProviders,
        IConnectedMailboxRepository connectedAccounts,
        CancellationToken ct
    )
    {
        var systemLookup = await systemProviders.GetEnabledDefaultAsync(ct);

        // La misma fuente que usa el envío (IConnectedMailboxResolver): si la pantalla mirara otra cosa,
        // podría decir "configurado" de un buzón por el que el correo no sale — que es exactamente el
        // tipo de incoherencia que hizo falta un día entero para diagnosticar en el carril anterior.
        var account = await connectedAccounts.FindActiveByTenantIdAsync(query.TenantId, ct);

        return new ProviderStatusDto(
            systemLookup.IsSuccess,
            account is not null,
            account is null ? null : new ConnectedMailboxSummary(account.FromAddress, account.ProviderCode)
        );
    }
}
