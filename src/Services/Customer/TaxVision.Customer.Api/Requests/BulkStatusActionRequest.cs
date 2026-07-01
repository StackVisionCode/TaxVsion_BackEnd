namespace TaxVision.Customer.Api.Requests;

public sealed record BulkStatusActionRequest(IReadOnlyList<Guid> CustomerIds, string? Reason);
