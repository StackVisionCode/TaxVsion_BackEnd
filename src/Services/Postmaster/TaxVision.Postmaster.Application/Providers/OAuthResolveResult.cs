using TaxVision.Postmaster.Application.Abstractions;

namespace TaxVision.Postmaster.Application.Providers;

public sealed record OAuthResolveResult(OAuthResolutionStatus Status, ResolvedOAuthProvider? Provider, string? Reason);
