using System.Text.Json.Serialization;
using TaxVision.Auth.Application.Credentials.Administration;
using TaxVision.Auth.Domain.Credentials;

namespace TaxVision.Auth.Application.Customers;

/// <summary>
/// Cuerpo M2M Customer → Auth para las acciones sobre la credencial del portal de un cliente (03 §15). Customer ya
/// aplicó el step-up y reenvía la marca de reautenticación, la IP real y el UA del actor para la auditoría.
/// </summary>
public sealed record PortalCredentialCommandEnvelope(
    Guid RequestedByUserId,
    DateTime ReauthenticatedAtUtc,
    string? ActorIpAddress,
    string? ActorUserAgent,
    [property: JsonConverter(typeof(ReasonCodeJsonConverter))] CredentialAdministrationReasonCode? ReasonCode = null,
    string? ReasonNote = null,
    bool? IdentityConfirmed = null
);
