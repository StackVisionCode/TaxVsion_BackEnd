using BuildingBlocks.Results;

namespace TaxVision.Tenant.Domain.Profiles;

/// <summary>Resultado de importar un perfil viejo: el perfil (null si no quedó nada útil) y los campos descartados.</summary>
public sealed record OfficeProfileImport(OfficeProfile? Profile, IReadOnlyList<Error> DroppedFields);
