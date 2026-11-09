namespace TaxVision.Connectors.Application.Providers;

/// <summary>Una página de ids del INBOX por fecha (re-sincronización). NextPageToken null = última página.</summary>
public sealed record InboxMessageIdPage(IReadOnlyList<string> MessageIds, string? NextPageToken);
