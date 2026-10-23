namespace TaxVision.Tasks.Infrastructure.Observability;

// Foto de lo atascado en Client Forms; la leen los gauges hasta la próxima lectura.
public sealed record ClientFormsBacklog(
    long AttachmentsPendingStale,
    long AwaitingReviewUnderOneDay,
    long AwaitingReviewOneToThreeDays,
    long AwaitingReviewOverThreeDays,
    long ActionRunsPendingStale
);
