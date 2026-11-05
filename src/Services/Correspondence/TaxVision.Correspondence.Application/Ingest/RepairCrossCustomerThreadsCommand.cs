namespace TaxVision.Correspondence.Application.Ingest;

/// <summary>
/// Reposición única de COR-02 (D-COR-1, un hilo por cliente): saca de cada hilo los correos de otro cliente y los
/// pasa al hilo de su propio cliente. Idempotente: un correo ya movido no vuelve a cumplir la condición.
/// </summary>
public sealed record RepairCrossCustomerThreadsCommand(Guid TenantId);

public sealed record RepairCrossCustomerThreadsResult(int MovedEmails, int CreatedThreads);
