using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Infrastructure.Providers.Assets;

namespace TaxVision.Postmaster.Infrastructure.Providers.Connectors;

/// <summary>
/// M2M de solo lectura a Connectors (<c>POST internal/accounts/addresses</c>, el mismo que usa Correspondence) para
/// saber de quién es cada buzón. Mismo adquirente de tokens que <see cref="ConnectorsSendClient"/>.
/// </summary>
public sealed class ConnectorsMailboxDirectoryClient(
    HttpClient httpClient,
    IPostmasterServiceTokenAcquirer tokenAcquirer,
    ILogger<ConnectorsMailboxDirectoryClient> logger
) : IConnectorsMailboxDirectory
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Result<IReadOnlyList<ConnectorsMailboxOwner>>> ListAsync(Guid tenantId, CancellationToken ct)
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<IReadOnlyList<ConnectorsMailboxOwner>>(
                new Error("ConnectorsMailboxDirectory.ServiceAuthUnavailable", "No service token for Connectors.")
            );

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "internal/accounts/addresses")
            {
                Content = JsonContent.Create(new { tenantId }, options: Json),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return Result.Failure<IReadOnlyList<ConnectorsMailboxOwner>>(
                    new Error(
                        "ConnectorsMailboxDirectory.UnexpectedStatus",
                        $"Connectors returned HTTP {(int)response.StatusCode}."
                    )
                );

            var body = await response.Content.ReadFromJsonAsync<AddressesDto>(Json, ct);
            IReadOnlyList<ConnectorsMailboxOwner> accounts = body?.Accounts ?? [];
            return Result.Success(accounts);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Connectors mailbox owners unavailable for tenant {TenantId}.", tenantId);
            return Result.Failure<IReadOnlyList<ConnectorsMailboxOwner>>(
                new Error("ConnectorsMailboxDirectory.Unavailable", "Connectors did not respond.")
            );
        }
    }

    private sealed record AddressesDto(IReadOnlyList<ConnectorsMailboxOwner>? Accounts);
}
