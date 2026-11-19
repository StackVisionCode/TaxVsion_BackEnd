namespace TaxVision.Tasks.Application.Work;

/// <summary>Rutas del CRM a las que lleva cada elemento; el módulo dueño revalida al abrir.</summary>
public static class WorkLinks
{
    public static string Task(Guid taskId) => $"/task?taskId={taskId}";

    public static string ClientRequest(Guid customerId, Guid requestId) =>
        $"/clients/{customerId}?tab=work&requestId={requestId}";
}
