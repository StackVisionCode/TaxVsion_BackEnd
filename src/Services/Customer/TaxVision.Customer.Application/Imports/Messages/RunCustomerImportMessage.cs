namespace TaxVision.Customer.Application.Imports.Messages;

/// <summary>
/// Mensaje publicado por StartCustomerImportHandler que el worker (Wolverine) procesa en background.
/// El worker corre en el mismo servicio Customer pero fuera del request HTTP, asi el endpoint
/// retorna 202 inmediato y el procesamiento pesado vive aparte.
/// </summary>
public sealed record RunCustomerImportMessage(Guid ImportAttemptId);
