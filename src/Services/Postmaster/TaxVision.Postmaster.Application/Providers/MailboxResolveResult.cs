using TaxVision.Postmaster.Application.Abstractions;

namespace TaxVision.Postmaster.Application.Providers;

public sealed record MailboxResolveResult(MailboxResolutionStatus Status, ResolvedMailbox? Provider, string? Reason);
