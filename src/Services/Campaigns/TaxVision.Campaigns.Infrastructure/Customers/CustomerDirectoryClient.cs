using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using TaxVision.Campaigns.Application.Contacts.Abstractions;

namespace TaxVision.Campaigns.Infrastructure.Customers;

/// <summary>
/// Crea clientes en el servicio <c>Customer</c> en nombre del usuario que importa (on-behalf-of):
/// reenvía el <c>Authorization: Bearer</c> de la sesión (que el controller leyó de la petición) a
/// <c>POST /customers</c>. Ese endpoint solo admite actores humanos (TenantEmployee/Admin/PlatformAdmin),
/// por eso NO se usa token de servicio (M2M). Todos los servicios comparten issuer/audience
/// (<c>TaxVision.Auth</c> / <c>TaxVision.Services</c>), así que el token de la sesión vale igual en
/// Customer. Tolerante a fallos: cualquier error se mapea a un desenlace y el import no se cae por eso.
/// </summary>
public sealed class CustomerDirectoryClient(HttpClient http, ILogger<CustomerDirectoryClient> logger)
    : ICustomerDirectoryClient
{
    public async Task<CustomerProvisionResult> CreateIndividualAsync(
        Guid tenantId,
        string? callerBearerToken,
        string? name,
        string? email,
        string? phoneE164,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(email))
            return new CustomerProvisionResult(CustomerProvisionOutcome.SkippedNoEmail);

        if (string.IsNullOrWhiteSpace(callerBearerToken))
        {
            logger.LogWarning(
                "No caller bearer token; cannot provision customer for tenant {TenantId}.",
                tenantId
            );
            return new CustomerProvisionResult(CustomerProvisionOutcome.Unavailable);
        }

        var (first, last) = SplitName(name, email);
        var payload = new CreateCustomerPayload(
            Kind: "Individual",
            FirstName: first,
            LastName: last,
            PrimaryEmail: email.Trim(),
            PrimaryPhone: string.IsNullOrWhiteSpace(phoneE164) ? null : phoneE164,
            Language: "Es",
            PreferredChannel: "Email",
            Overwrite: false
        );

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "customers")
            {
                Content = JsonContent.Create(payload),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", callerBearerToken);

            using var response = await http.SendAsync(request, ct);

            switch (response.StatusCode)
            {
                case HttpStatusCode.Created:
                case HttpStatusCode.OK:
                    var created = await SafeReadIdAsync(response, ct);
                    return new CustomerProvisionResult(CustomerProvisionOutcome.Created, created);
                case HttpStatusCode.Conflict:
                    return new CustomerProvisionResult(CustomerProvisionOutcome.AlreadyExisted);
                case HttpStatusCode.Forbidden:
                case HttpStatusCode.Unauthorized:
                    return new CustomerProvisionResult(CustomerProvisionOutcome.Forbidden);
                default:
                    logger.LogWarning(
                        "Customer create returned {Status} for tenant {TenantId}; reporting as unavailable.",
                        (int)response.StatusCode,
                        tenantId
                    );
                    return new CustomerProvisionResult(CustomerProvisionOutcome.Unavailable);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not reach Customer to provision a client for tenant {TenantId}.", tenantId);
            return new CustomerProvisionResult(CustomerProvisionOutcome.Unavailable);
        }
    }

    private static async Task<Guid?> SafeReadIdAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var dto = await response.Content.ReadFromJsonAsync<CreatedCustomerDto>(ct);
            return dto?.Id;
        }
        catch
        {
            return null; // el id es informativo; su ausencia no cambia el desenlace.
        }
    }

    /// <summary>
    /// Parte el nombre en nombre/apellido (Customer exige ambos para Individual). Tolerante: con un solo
    /// token lo usa en ambos; sin nombre deriva del local-part del email; nunca devuelve vacío para no
    /// provocar un 400 por nombre.
    /// </summary>
    private static (string First, string Last) SplitName(string? name, string email)
    {
        var source = !string.IsNullOrWhiteSpace(name) ? name!.Trim() : LocalPart(email);
        var tokens = source.Split(
            [' ', '.', '_', '-'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        return tokens.Length switch
        {
            >= 2 => (tokens[0], string.Join(' ', tokens[1..])),
            1 => (tokens[0], tokens[0]),
            _ => ("Cliente", "Importado"),
        };
    }

    private static string LocalPart(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 ? email[..at] : email;
    }

    // El serializador de Customer usa JsonStringEnumConverter → los enums van como nombre (string).
    private sealed record CreateCustomerPayload(
        string Kind,
        string FirstName,
        string LastName,
        string PrimaryEmail,
        string? PrimaryPhone,
        string Language,
        string PreferredChannel,
        bool Overwrite
    );

    private sealed record CreatedCustomerDto(Guid Id);
}
