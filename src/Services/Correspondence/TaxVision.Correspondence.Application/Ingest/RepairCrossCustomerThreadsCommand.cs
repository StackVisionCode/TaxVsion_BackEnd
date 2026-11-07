namespace TaxVision.Correspondence.Application.Ingest;

/// <summary>
/// Reposición única de COR-02 (D-COR-1, un hilo por cliente): saca de cada hilo los correos de otro cliente y los
/// pasa al hilo de su propio cliente. En seco por defecto: solo cuenta. Idempotente: un correo ya movido no vuelve a
/// cumplir la condición.
/// </summary>
public sealed record RepairCrossCustomerThreadsCommand(Guid TenantId, bool DryRun = true);

/// <summary>Lo encontrado y lo hecho. <c>FailedThreadIds</c>: hilos cuya unidad falló; se pueden repetir.</summary>
public sealed record RepairCrossCustomerThreadsReport(
    bool DryRun,
    int ThreadsFound,
    int EmailsFound,
    int ThreadsRepaired,
    int EmailsMoved,
    int ThreadsCreated,
    IReadOnlyList<Guid> FailedThreadIds
);

/// <summary>Una unidad de la reparación: un hilo, en su propia transacción.</summary>
public sealed record RepairCrossCustomerThreadCommand(Guid TenantId, Guid ThreadId);

public sealed record RepairCrossCustomerThreadResult(int MovedEmails, int CreatedThreads);
