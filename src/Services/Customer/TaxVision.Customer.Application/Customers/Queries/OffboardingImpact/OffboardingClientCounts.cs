namespace TaxVision.Customer.Application.Customers.Queries.OffboardingImpact;

/// <summary>Clientes que toca el retiro de un empleado, por lo que les pasa: los primary activos se traspasan
/// al sucesor (o quedan sin preparador), los accesos adicionales se revocan y los archivados solo lo sueltan.</summary>
public sealed record OffboardingClientCounts(int PrimaryClients, int AdditionalAccess, int ArchivedClients)
{
    public static readonly OffboardingClientCounts None = new(0, 0, 0);
}
