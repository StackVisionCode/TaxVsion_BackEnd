namespace TaxVision.Customer.Application.Customers.Commands.RemoveRelation;

public sealed record RemoveRelationCommand(Guid TenantId, Guid CustomerId, Guid RelationId, Guid ModifiedByUserId);
