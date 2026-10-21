namespace TaxVision.Tasks.Api.Requests.ClientForms;

public sealed record SetFormModuleAvailabilityRequest(bool Enabled, string? Note);
