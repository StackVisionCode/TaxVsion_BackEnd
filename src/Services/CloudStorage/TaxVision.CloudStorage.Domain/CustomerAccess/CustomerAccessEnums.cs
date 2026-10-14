namespace TaxVision.CloudStorage.Domain.CustomerAccess;

/// <summary>Qué item alcanza una regla deny: una carpeta, un archivo, o (futuro) el scope del tenant para presets.</summary>
public enum AccessResourceType
{
    Folder = 1,
    File = 2,

    /// <summary>Reservado para presets a nivel tenant (ResourceId == TenantId); los endpoints de F3 no lo aceptan.</summary>
    TenantScope = 3,
}

/// <summary>Capability atómica negada por una regla. Deny gana a allow; "Visible" oculta el item entero.</summary>
public enum AccessCapability
{
    Visible = 1,
    Preview = 2,
    Download = 3,
    Upload = 4,
    Delete = 5,
    ShareExternal = 6,
}

/// <summary>Qué pasó: la regla se creó/actualizó, o se removió. Una fila por mutación.</summary>
public enum AccessRuleAction
{
    Created = 1,
    Removed = 2,
}
