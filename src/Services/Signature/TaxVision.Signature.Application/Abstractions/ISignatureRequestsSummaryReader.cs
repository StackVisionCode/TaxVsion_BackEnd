namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Período en UTC [FromUtc, ToUtc). <paramref name="AssignedTo"/> null = toda la oficina. <paramref name="CreatedBy"/>
/// = solo las que creó esa persona (alcance "Me").
/// </summary>
public sealed record SignatureRequestsSummaryFilter(
    Guid TenantId,
    Guid? AssignedTo,
    DateTime FromUtc,
    DateTime ToUtc,
    DateTime NowUtc,
    Guid? CreatedBy = null
);

/// <summary>Conteos en vivo sobre las solicitudes, con el recorte del listado.</summary>
public sealed record SignatureRequestsLiveCounts(
    int Draft,
    int InProgress,
    int ExpiringWithin48h,
    int Sent,
    int Completed,
    int Expired,
    int Canceled,
    double? AvgMinutesToSign
);

public interface ISignatureRequestsSummaryReader
{
    Task<SignatureRequestsLiveCounts> ReadAsync(SignatureRequestsSummaryFilter filter, CancellationToken ct = default);
}
