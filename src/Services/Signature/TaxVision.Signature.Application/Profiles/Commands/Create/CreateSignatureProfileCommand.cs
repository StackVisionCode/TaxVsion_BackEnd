namespace TaxVision.Signature.Application.Profiles.Commands.Create;

/// <summary>
/// Crea una firma reutilizable. <paramref name="OwnerUserId"/> null = firma de oficina (requiere
/// admin); con valor = firma personal (debe ser el propio actor). El PNG llega como bytes y se sube
/// a CloudStorage antes de persistir el metadato.
/// </summary>
public sealed record CreateSignatureProfileCommand(
    Guid TenantId,
    Guid ActorUserId,
    bool ActorIsAdmin,
    // F4 — el controller resuelve el permiso vía IUserPermissionsSource y lo pasa aquí.
    bool ActorHasSignOwn,
    Guid? OwnerUserId,
    string Label,
    byte[] Content
);
