using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Infrastructure.Security;

namespace TaxVision.Auth.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAuthInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<AuthDbContext>(options => options.UseSqlServer(connectionString));

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<RefreshTokenOptions>(configuration.GetSection(RefreshTokenOptions.SectionName));

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<AuthDbContext>());
        services.AddScoped<ITenantRegistry, TenantRegistry>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<IInvitationTokenService, InvitationTokenService>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();

        return services;
    }
}
