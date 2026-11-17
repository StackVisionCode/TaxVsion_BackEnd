using BuildingBlocks.Domain;

namespace TaxVision.Growth.Infrastructure.Persistence.Referrals;

/// <summary>
/// Nombre de cada oficina, para que el referidor vea a quién refirió. La llenan los eventos de Tenant. El referido de un
/// alta es su <c>OnboardingId</c>, así que se busca por el tenant o por el alta que lo creó. Cada campo guarda su marca
/// de orden: un evento más viejo no pisa lo guardado.
/// </summary>
public sealed class ReferredOfficeProjection : TenantEntity
{
    public const int NameMaxLength = 200;

    private ReferredOfficeProjection() { }

    public Guid? OnboardingId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTime NameUpdatedAtUtc { get; private set; }
    public string? DisplayName { get; private set; }
    public long ProfileVersion { get; private set; }

    /// <summary>HMAC del email normalizado del dueño (el mismo sujeto Buyer de los códigos). Nunca el email.</summary>
    public string? OwnerSubjectId { get; private set; }

    /// <summary>El email del dueño es de un dominio desechable.</summary>
    public bool OwnerEmailDisposable { get; private set; }

    public static ReferredOfficeProjection Create(Guid officeTenantId)
    {
        var projection = new ReferredOfficeProjection { Id = Guid.NewGuid() };
        projection.SetTenant(officeTenantId);
        return projection;
    }

    public void AttachOnboarding(Guid? onboardingId) => OnboardingId ??= onboardingId;

    // La identidad del dueño se fija con la creación de la oficina y no se reemplaza.
    public void AttachOwner(string? ownerSubjectId, bool disposable)
    {
        if (OwnerSubjectId is not null || ownerSubjectId is null)
            return;

        OwnerSubjectId = ownerSubjectId;
        OwnerEmailDisposable = disposable;
    }

    public void ApplyName(string name, DateTime updatedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(name) || updatedAtUtc <= NameUpdatedAtUtc)
            return;

        Name = Clip(name)!;
        NameUpdatedAtUtc = updatedAtUtc;
    }

    public void ApplyDisplayName(string? displayName, long version)
    {
        if (version <= ProfileVersion)
            return;

        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : Clip(displayName);
        ProfileVersion = version;
    }

    private static string? Clip(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length > NameMaxLength ? trimmed[..NameMaxLength] : trimmed;
    }
}
