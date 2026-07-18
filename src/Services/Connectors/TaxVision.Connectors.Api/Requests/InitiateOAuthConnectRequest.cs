using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Api.Requests;

public sealed record InitiateOAuthConnectRequest(ProviderCode ProviderCode);
