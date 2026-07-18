using TaxVision.Postmaster.Domain.Suppression;

namespace TaxVision.Postmaster.Api.Requests;

public sealed record AddSuppressionEntryRequest(string Address, SuppressionReason Reason, string? Notes);
