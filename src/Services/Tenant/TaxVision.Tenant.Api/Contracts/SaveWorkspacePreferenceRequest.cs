using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaxVision.Tenant.Api.Contracts;

/// <summary>Cuerpo de los PUT de preferencias. Los tres campos son obligatorios; sin ellos el binder responde 400.</summary>
public sealed record SaveWorkspacePreferenceRequest(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] long ExpectedVersion,
    [property: JsonRequired] JsonElement Payload
);
