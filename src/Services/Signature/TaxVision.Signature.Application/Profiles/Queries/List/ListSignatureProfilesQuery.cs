namespace TaxVision.Signature.Application.Profiles.Queries.List;

/// <summary>Lista las firmas visibles para un usuario: la de oficina y, si puede, las suyas.</summary>
public sealed record ListSignatureProfilesQuery(
    Guid TenantId,
    Guid UserId,
    bool ActorIsAdmin,
    // F4 — el controller resuelve el permiso vía IUserPermissionsSource y lo pasa aquí.
    bool ActorHasSignOwn,
    bool IncludeArchived
);

/// <summary><paramref name="CanManageOwnSignature"/> le dice al front si mostrar/permitir firmas personales.</summary>
public sealed record ListSignatureProfilesResult(
    IReadOnlyList<SignatureProfileResponse> Profiles,
    bool CanManageOwnSignature
);
