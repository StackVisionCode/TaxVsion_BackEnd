namespace TaxVision.Signature.Application.Profiles.Commands.SetDefault;

/// <summary>Marca una firma como la por defecto de su ámbito (desmarca la anterior).</summary>
public sealed record SetDefaultSignatureProfileCommand(
    Guid TenantId,
    Guid ProfileId,
    Guid ActorUserId,
    bool ActorIsAdmin
);
