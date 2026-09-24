namespace TaxVision.Signature.Application.Profiles.Commands.Rename;

/// <summary>Renombra una firma reutilizable (solo la etiqueta; no toca la imagen).</summary>
public sealed record RenameSignatureProfileCommand(
    Guid TenantId,
    Guid ProfileId,
    Guid ActorUserId,
    bool ActorIsAdmin,
    string Label
);
