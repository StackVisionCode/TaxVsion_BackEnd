namespace TaxVision.Customer.Api.Requests;

public sealed record BulkAssignCustomersRequest(Guid UserId, IReadOnlyList<Guid> CustomerIds);
