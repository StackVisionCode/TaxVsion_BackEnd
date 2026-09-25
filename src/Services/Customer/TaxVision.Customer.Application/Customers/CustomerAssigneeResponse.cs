namespace TaxVision.Customer.Application.Customers;

// Un miembro del staff asignado a un cliente. El nombre/avatar los resuelve el front con el directorio
// de empleados; acá solo el userId y si es el preparador principal (responsable).
public sealed record CustomerAssigneeResponse(Guid UserId, bool IsPrimary);
