using BuildingBlocks.Authorization.SlotGating;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Infrastructure.SlotGating;

public static class SeatSlotGatingRegistration
{
    public static IServiceCollection AddSqlSeatSlotGate(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISeatSlotGate>(_ => new SqlSeatSlotGate(configuration.GetConnectionString("Auth")));
        return services;
    }
}
