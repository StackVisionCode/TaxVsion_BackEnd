using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.CustomerAccess;

namespace TaxVision.CloudStorage.Application.CustomerAccess.Queries.GetRulesByCustomer;

public static class GetRulesByCustomerHandler
{
    public static async Task<Result<IReadOnlyList<CustomerAccessRuleView>>> Handle(
        GetRulesByCustomerQuery query,
        ICustomerAccessRuleRepository rules,
        IFolderRepository folders,
        IFileObjectRepository files,
        ICustomerAssignmentProjectionStore assignments,
        CancellationToken ct
    )
    {
        var auth = await EnsureAuthorized(query, assignments, ct);
        if (auth.IsFailure)
            return Result.Failure<IReadOnlyList<CustomerAccessRuleView>>(auth.Error);

        var raw = await rules.ListByCustomerAsync(query.TenantId, query.CustomerId, ct);

        // Hidrata nombres por tipo con un batch read por repo. Lo que no esta ⇒ null (orphan tras
        // borrado) y el UI pinta "Deleted folder/file". TenantScope nunca tiene nombre real.
        var folderIds = raw.Where(r => r.ResourceType == AccessResourceType.Folder)
            .Select(r => r.ResourceId)
            .Distinct()
            .ToList();
        var fileIds = raw.Where(r => r.ResourceType == AccessResourceType.File)
            .Select(r => r.ResourceId)
            .Distinct()
            .ToList();
        var folderNames = await folders.ListNamesByIdsAsync(query.TenantId, folderIds, ct);
        var fileNames = await files.ListNamesByIdsAsync(query.TenantId, fileIds, ct);

        IReadOnlyList<CustomerAccessRuleView> mapped = raw.Select(r =>
                CustomerAccessRuleMapper.Map(r, ResolveName(r, folderNames, fileNames))
            )
            .ToList();
        return Result.Success(mapped);
    }

    private static string? ResolveName(
        CustomerDocumentAccessRule rule,
        IReadOnlyDictionary<Guid, string> folderNames,
        IReadOnlyDictionary<Guid, string> fileNames
    ) =>
        rule.ResourceType switch
        {
            AccessResourceType.Folder => folderNames.TryGetValue(rule.ResourceId, out var n) ? n : null,
            AccessResourceType.File => fileNames.TryGetValue(rule.ResourceId, out var n) ? n : null,
            _ => null,
        };

    private static async Task<Result> EnsureAuthorized(
        GetRulesByCustomerQuery query,
        ICustomerAssignmentProjectionStore assignments,
        CancellationToken ct
    )
    {
        if (query.HasManageAny)
            return Result.Success();
        if (!query.HasManageAssigned)
            return Result.Failure(CustomerAccessErrors.Forbidden);

        var assigned = await assignments.IsAnyAssignedToUserAsync(
            query.TenantId,
            query.ActorUserId,
            [query.CustomerId],
            ct
        );
        return assigned ? Result.Success() : Result.Failure(CustomerAccessErrors.Forbidden);
    }
}
