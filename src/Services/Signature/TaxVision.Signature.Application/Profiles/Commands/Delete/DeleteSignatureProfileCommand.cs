namespace TaxVision.Signature.Application.Profiles.Commands.Delete;

/// <summary>Borra en firme una firma reutilizable (quita el metadato; el objeto en storage se recicla aparte).</summary>
public sealed record DeleteSignatureProfileCommand(Guid TenantId, Guid ProfileId, Guid ActorUserId, bool ActorIsAdmin);
