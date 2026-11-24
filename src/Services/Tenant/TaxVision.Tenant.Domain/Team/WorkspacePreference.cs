using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Domain.Team;

/// <summary>
/// Preferencia de espacio de trabajo de un empleado, o la plantilla de la oficina si <see cref="UserId"/> es nulo.
/// Restablecer no borra la fila: deja el contenido nulo y sube la versión, así una pestaña vieja nunca pisa un
/// cambio nuevo. Solo el retiro del empleado borra sus filas.
/// </summary>
public sealed class WorkspacePreference : TenantEntity
{
    private WorkspacePreference() { }

    [UserReference(
        UserReferenceKind.Private,
        HandledBy = "WorkspacePreferencesUserOffboardedConsumer",
        Note = "Se borra."
    )]
    public Guid? UserId { get; private set; }

    public string PreferenceKey { get; private set; } = string.Empty;
    public int SchemaVersion { get; private set; }

    /// <summary>JSON canónico; nulo = sin contenido (restablecida o plantilla quitada).</summary>
    public string? PayloadJson { get; private set; }

    /// <summary>Token de concurrencia; sube en cada cambio y nunca retrocede.</summary>
    public long Version { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<WorkspacePreference> Create(
        Guid tenantId,
        Guid? userId,
        string key,
        IWorkspacePreferenceContent content,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<WorkspacePreference>(WorkspacePreferenceErrors.TenantRequired);
        if (userId == Guid.Empty)
            return Result.Failure<WorkspacePreference>(WorkspacePreferenceErrors.OwnerInvalid);
        if (!WorkspacePreferenceKeys.IsKnown(key))
            return Result.Failure<WorkspacePreference>(WorkspacePreferenceErrors.UnknownKey);

        var preference = new WorkspacePreference
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PreferenceKey = key,
            SchemaVersion = content.SchemaVersion,
            PayloadJson = content.ToCanonicalJson(),
            Version = 1,
            UpdatedAtUtc = nowUtc,
        };
        preference.SetTenant(tenantId);
        return Result.Success(preference);
    }

    /// <summary>Reemplaza el contenido; <c>false</c> si es igual al guardado (no escribe ni sube versión).</summary>
    public Result<bool> Replace(IWorkspacePreferenceContent content, long expectedVersion, DateTime nowUtc)
    {
        if (expectedVersion != Version)
            return Result.Failure<bool>(WorkspacePreferenceErrors.VersionConflict);

        var json = content.ToCanonicalJson();
        if (string.Equals(PayloadJson, json, StringComparison.Ordinal) && SchemaVersion == content.SchemaVersion)
            return Result.Success(false);

        PayloadJson = json;
        SchemaVersion = content.SchemaVersion;
        Touch(nowUtc);
        return Result.Success(true);
    }

    /// <summary>Deja la fila sin contenido; <c>false</c> si ya estaba vacía.</summary>
    public Result<bool> Clear(long expectedVersion, DateTime nowUtc)
    {
        if (expectedVersion != Version)
            return Result.Failure<bool>(WorkspacePreferenceErrors.VersionConflict);
        if (PayloadJson is null)
            return Result.Success(false);

        PayloadJson = null;
        Touch(nowUtc);
        return Result.Success(true);
    }

    private void Touch(DateTime nowUtc)
    {
        Version++;
        UpdatedAtUtc = nowUtc;
    }
}
