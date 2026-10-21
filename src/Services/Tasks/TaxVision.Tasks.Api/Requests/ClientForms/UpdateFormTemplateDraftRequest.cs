using System.Text.Json;

namespace TaxVision.Tasks.Api.Requests.ClientForms;

public sealed record UpdateFormTemplateDraftRequest(JsonElement Definition, string RowVersion);
