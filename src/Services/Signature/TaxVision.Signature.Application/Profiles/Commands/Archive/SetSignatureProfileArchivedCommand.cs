namespace TaxVision.Signature.Application.Profiles.Commands.Archive;

/// <summary>Archiva o restaura una firma. Archivar la saca del picker; si era default deja de serlo.</summary>
public sealed record SetSignatureProfileArchivedCommand(
    Guid TenantId,
    Guid ProfileId,
    Guid ActorUserId,
    bool ActorIsAdmin,
    bool Archived
);
