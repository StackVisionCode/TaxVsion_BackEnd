namespace TaxVision.Tasks.Api.Requests.ClientForms;

public sealed record RenameFormTemplateRequest(string? Name, string? Description, string RowVersion);
