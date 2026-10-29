namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

// El snapshot cifrado del Submission Summary. Viaja dentro del ProtectedPayload, ligado a tenant,
// cliente y request: minimizado, ya enmascarado y sólo con lo imprimible. Compartido entre Tasks
// (lo arma) y Documents (lo descifra y lo renderiza).
public sealed record SubmissionSummaryPayload
{
    public required Guid TenantId { get; init; }
    public Guid? CustomerId { get; init; }
    public required Guid FormRequestId { get; init; }
    public required SubmissionSummarySnapshot Snapshot { get; init; }
}

public sealed record SubmissionSummarySnapshot
{
    public required string Title { get; init; }
    public string? CustomerDisplayName { get; init; }
    public required int VersionNumber { get; init; }
    public required int RevisionNumber { get; init; }
    public DateTime? SubmittedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public IReadOnlyList<SubmissionSummaryPage> Pages { get; init; } = [];
    public IReadOnlyList<SubmissionSummaryAttachment> Attachments { get; init; } = [];
}

public sealed record SubmissionSummaryPage(string? Title, IReadOnlyList<SubmissionSummarySection> Sections);

public sealed record SubmissionSummarySection(
    string? Title,
    IReadOnlyList<SubmissionSummaryItem> Items,
    IReadOnlyList<SubmissionSummaryRow> Rows
);

public sealed record SubmissionSummaryItem(string Label, string Value);

public sealed record SubmissionSummaryRow(string Label, IReadOnlyList<SubmissionSummaryItem> Items);

public sealed record SubmissionSummaryAttachment(string Label, string FileName);
