namespace TaxVision.Tasks.ClientForms.Application.Mapping.Abstractions;

// Lectura de plataforma, sin tenant: cuánto depende todavía de un destino del catálogo.
public interface IFormCatalogUsageReader
{
    Task<FormCatalogUsageCounts> CountAsync(string targetKey, DateTime nowUtc, CancellationToken ct = default);
}
