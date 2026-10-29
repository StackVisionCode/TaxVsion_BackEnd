using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;

/// <summary>
/// Única lectura entre tenants del módulo: plantillas del tenant de plataforma, listadas y con una
/// versión publicada. Toda implementación filtra por <c>TenantId == PlatformTenant.Id</c>.
/// </summary>
public interface IFormLibraryRepository
{
    Task<IReadOnlyList<LibraryTemplateRecord>> ListAsync(CancellationToken ct = default);
    Task<LibraryTemplateDetailRecord?> GetByKeyAsync(string libraryKey, CancellationToken ct = default);
}

public sealed record LibraryTemplateRecord(
    string LibraryKey,
    string Name,
    string? Description,
    FormPurpose Purpose,
    int VersionNumber
);

// Agrega el JSON de la última versión publicada, para preview y clonado.
public sealed record LibraryTemplateDetailRecord(
    Guid SourceTemplateId,
    string LibraryKey,
    string Name,
    string? Description,
    FormPurpose Purpose,
    int VersionNumber,
    string DefinitionJson
);
