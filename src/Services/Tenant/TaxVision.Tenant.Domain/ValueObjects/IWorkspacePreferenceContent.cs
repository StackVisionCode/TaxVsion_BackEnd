namespace TaxVision.Tenant.Domain.ValueObjects;

/// <summary>Contenido ya validado de una preferencia: su esquema y su JSON canónico.</summary>
public interface IWorkspacePreferenceContent
{
    int SchemaVersion { get; }

    string ToCanonicalJson();
}
