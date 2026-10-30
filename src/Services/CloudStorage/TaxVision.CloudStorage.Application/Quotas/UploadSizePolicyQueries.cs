using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Application.Quotas;

public sealed record ListUploadSizePoliciesQuery;

/// <summary>Todas las filas administradas + el techo técnico, para que el admin sepa hasta dónde puede subir un tope.</summary>
public sealed record UploadSizePoliciesResponse(long TechnicalMaxBytes, IReadOnlyList<UploadSizePolicyView> Policies);

public static class ListUploadSizePoliciesHandler
{
    public static async Task<UploadSizePoliciesResponse> Handle(
        ListUploadSizePoliciesQuery query,
        IUploadSizePolicyRepository policies,
        CancellationToken ct
    )
    {
        var rows = await policies.ListAsync(ct);
        return new UploadSizePoliciesResponse(
            UploadSizePolicy.TechnicalMaxBytes,
            rows.Select(UploadSizePolicyMapper.Map).ToList()
        );
    }
}

public sealed record ListUploadSizePolicyAuditQuery(int Skip, int Take);

public sealed record UploadSizePolicyAuditView(
    Guid Id,
    string Scope,
    string FolderType,
    string? PlanCode,
    Guid? TenantId,
    long? OldMaxFileSizeBytes,
    long? NewMaxFileSizeBytes,
    Guid ActorUserId,
    DateTime OccurredAtUtc
);

public static class ListUploadSizePolicyAuditHandler
{
    public static async Task<IReadOnlyList<UploadSizePolicyAuditView>> Handle(
        ListUploadSizePolicyAuditQuery query,
        IUploadSizePolicyAuditRepository audit,
        CancellationToken ct
    )
    {
        var entries = await audit.ListAsync(Math.Max(0, query.Skip), Math.Clamp(query.Take, 1, 200), ct);
        return entries
            .Select(e => new UploadSizePolicyAuditView(
                e.Id,
                e.Scope.ToString(),
                e.FolderType.ToString(),
                e.PlanCode,
                e.TenantId,
                e.OldMaxFileSizeBytes,
                e.NewMaxFileSizeBytes,
                e.ActorUserId,
                e.OccurredAtUtc
            ))
            .ToList();
    }
}
