namespace TaxVision.Tasks.Application.Tasks.Abstractions;

/// <summary>Ligar una tarea a un cliente exige poder verlo (misma asignación que filtra las listas).</summary>
public interface ITaskCustomerAccess
{
    Task<bool> CanLinkAsync(Guid tenantId, Guid userId, bool canViewAll, Guid customerId, CancellationToken ct);
}

public static class TaskCustomerAccessExtensions
{
    /// <summary>Sin cliente no hay nada que comprobar.</summary>
    public static async Task<bool> AllowsAsync(
        this ITaskCustomerAccess access,
        Guid tenantId,
        Guid userId,
        bool canViewAll,
        Guid? customerId,
        CancellationToken ct
    ) => customerId is not { } customer || await access.CanLinkAsync(tenantId, userId, canViewAll, customer, ct);
}
