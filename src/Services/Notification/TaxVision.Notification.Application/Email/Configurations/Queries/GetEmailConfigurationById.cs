using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Email.Configurations.Queries;

public sealed record GetEmailConfigurationByIdQuery(Guid ConfigurationId, Guid? TenantId);

public static class GetEmailConfigurationByIdHandler
{
    public static async Task<Result<EmailConfigurationResponse>> Handle(
        GetEmailConfigurationByIdQuery query,
        IEmailProviderConfigurationRepository repository,
        CancellationToken ct
    )
    {
        var config = await repository.GetByIdAsync(query.ConfigurationId, query.TenantId, ct);
        return config is null
            ? Result.Failure<EmailConfigurationResponse>(
                new Error("EmailConfiguration.NotFound", "Configuration not found.")
            )
            : Result.Success(EmailConfigurationMapper.ToResponse(config));
    }
}
