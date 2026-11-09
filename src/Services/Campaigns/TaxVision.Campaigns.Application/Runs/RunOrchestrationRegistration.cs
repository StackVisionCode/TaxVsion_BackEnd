using Microsoft.Extensions.DependencyInjection;
using TaxVision.Campaigns.Application.Runs.Audience;
using TaxVision.Campaigns.Application.Runs.Commands;
using TaxVision.Campaigns.Application.Scheduling.Commands;
using TaxVision.Campaigns.Application.Scheduling.Consumers;

namespace TaxVision.Campaigns.Application.Runs;

/// <summary>Colaboradores de los handlers de runs y agendados (CMP-13).</summary>
public static class RunOrchestrationRegistration
{
    public static IServiceCollection AddCampaignRunOrchestration(this IServiceCollection services)
    {
        services.AddScoped<CampaignAudienceService>();
        services.AddScoped<CampaignRunStarter>();
        services.AddScoped<CampaignRunDispatcher>();
        services.AddScoped<ScheduledRunOpener>();
        services.AddScoped<ScheduleOffboardingResolver>();
        return services;
    }
}
