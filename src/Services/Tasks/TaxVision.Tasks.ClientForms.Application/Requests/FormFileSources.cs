using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests;

/// <summary>Declaración a CloudStorage de los archivos de un form request: adjuntos y PDF resumen.</summary>
public static class FormFileSources
{
    public const string Service = "tasks";
    public const string ResourceType = "form_request";
    public const string AttachmentRole = "attachment";
    public const string SummaryRole = "summary_pdf";

    public static FileSourceDeclaredIntegrationEvent ForAttachment(
        FormRequest request,
        FormAttachment attachment,
        string correlationId
    ) => Declare(request.TenantId, request.Id, request.CustomerId, attachment.FileId, AttachmentRole, correlationId);

    public static FileSourceDeclaredIntegrationEvent ForSummary(
        FormRequest request,
        Guid fileId,
        string correlationId
    ) => Declare(request.TenantId, request.Id, request.CustomerId, fileId, SummaryRole, correlationId);

    // Un externo sin vincular no tiene cliente: RelatedCustomerId va nulo.
    public static FileSourceDeclaredIntegrationEvent Declare(
        Guid tenantId,
        Guid requestId,
        Guid? customerId,
        Guid fileId,
        string role,
        string correlationId
    ) =>
        new()
        {
            TenantId = tenantId,
            CorrelationId = correlationId,
            FileId = fileId,
            SourceService = Service,
            SourceResourceType = ResourceType,
            SourceResourceId = requestId,
            SourceRole = role,
            RelatedCustomerId = customerId,
        };
}
