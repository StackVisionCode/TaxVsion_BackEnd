namespace TaxVision.Customer.Application.Customers.Commands.BulkAssign;

// Resumen del reparto: pedidos, recién asignados, los que ya estaban, y los que no existen o están archivados.
public sealed record BulkAssignResponse(int Requested, int Assigned, int AlreadyAssigned, int NotFound);
