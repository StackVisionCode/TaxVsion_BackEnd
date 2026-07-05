using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Email.Configurations.Queries;

/// <summary>Lista las configuraciones visibles para el tenant (las suyas + las globales del SaaS).</summary>
public sealed record GetEmailConfigurationsQuery(Guid? TenantId, bool IncludeSystem = true);

public static class GetEmailConfigurationsHandler
{
    public static async Task<Result<IReadOnlyList<EmailConfigurationResponse>>> Handle(
        GetEmailConfigurationsQuery query,
        IEmailProviderConfigurationRepository repository,
        CancellationToken ct
    )
    {
        var items = await repository.ListAsync(query.TenantId, query.IncludeSystem, ct);
        IReadOnlyList<EmailConfigurationResponse> responses = items.Select(EmailConfigurationMapper.ToResponse).ToList();
        return Result.Success(responses);
    }
}
