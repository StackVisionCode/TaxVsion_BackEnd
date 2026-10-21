namespace TaxVision.Tasks.Api.Requests.ClientForms;

public sealed record RevertFormTemplateDraftRequest(int VersionNumber, string RowVersion);
