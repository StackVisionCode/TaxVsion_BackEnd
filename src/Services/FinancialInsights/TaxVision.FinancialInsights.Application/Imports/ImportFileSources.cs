using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.FinancialInsights.Domain.Imports;
using Wolverine;

namespace TaxVision.FinancialInsights.Application.Imports;

/// <summary>
/// Declaración a CloudStorage del archivo que adopta un lote: es un estado de cuenta del lote y del cliente
/// (par de <c>FileSourceCatalog</c>). FinancialInsights no guarda archivos, así que lo declara por evento.
/// </summary>
public static class ImportFileSources
{
    public const string SourceService = "financial_insights";
    public const string ResourceType = "financial_import_batch";
    public const string Role = "statement";

    public static FileSourceDeclaredIntegrationEvent Declaration(
        Guid tenantId,
        Guid batchId,
        Guid customerId,
        Guid fileId,
        string correlationId = ""
    ) =>
        new()
        {
            TenantId = tenantId,
            CorrelationId = correlationId,
            FileId = fileId,
            SourceService = SourceService,
            SourceResourceType = ResourceType,
            SourceResourceId = batchId,
            SourceRole = Role,
            RelatedCustomerId = customerId,
        };

    /// <summary>Una declaración por documento; se llama después de guardar el lote (sale con su commit).</summary>
    public static async Task DeclareAsync(IMessageBus bus, ImportBatch batch)
    {
        foreach (var document in batch.Documents)
            await bus.PublishAsync(Declaration(batch.TenantId, batch.Id, batch.CustomerId, document.FileId));
    }
}
