using System.Net.Http.Headers;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Invitations.Commands;
using TaxVision.Auth.Application.ServiceTokens;
using TaxVision.Auth.Application.TenantDomains;
using TaxVision.Auth.Application.Terms;
using TaxVision.Auth.Infrastructure.Cloudflare;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Infrastructure.Security;
using TaxVision.Auth.Infrastructure.Tenancy;

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
        services.Configure<InvitationOptions>(configuration.GetSection(InvitationOptions.SectionName));
        services.Configure<ServiceAuthOptions>(configuration.GetSection(ServiceAuthOptions.SectionName));
        services.Configure<TenantDomainOptions>(configuration.GetSection(TenantDomainOptions.SectionName));
        services.Configure<CloudflareOptions>(configuration.GetSection(CloudflareOptions.SectionName));
        services.Configure<TermsOptions>(configuration.GetSection(TermsOptions.SectionName));

        // Persistencia
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<AuthDbContext>());
        services.AddScoped<ITenantRegistry, TenantRegistry>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IMfaRepository, MfaRepository>();
        services.AddScoped<ICredentialTokenRepository, CredentialTokenRepository>();
        services.AddScoped<ITenantPlanLimitsStore, TenantPlanLimitsStore>();
        services.AddScoped<ITenantDomainRepository, TenantDomainRepository>();
        services.AddScoped<ITenantSubdomainReservationRepository, TenantSubdomainReservationRepository>();
        services.AddScoped<ITenantResolutionCache, TenantResolutionCache>();
        services.AddScoped<ITenantResolver, TenantResolver>();
        services.AddScoped<ITenantTermsAcceptanceRepository, TenantTermsAcceptanceRepository>();
        services.AddHttpClient<ICloudflareProvisioningClient, CloudflareProvisioningClient>(
            (provider, client) =>
            {
                var cloudflare = provider.GetRequiredService<IOptions<CloudflareOptions>>().Value;
                client.BaseAddress = new Uri(cloudflare.BaseUrl);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    cloudflare.ApiToken
                );
            }
        );
        services.AddScoped<AuthAuditStore>();
        services.AddScoped<IAuthAuditWriter>(provider => provider.GetRequiredService<AuthAuditStore>());
        services.AddScoped<IAuthAuditReader>(provider => provider.GetRequiredService<AuthAuditStore>());

        // Seguridad
        services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<IInvitationTokenService, InvitationTokenService>();
        services.AddSingleton<ISecureTokenService, SecureTokenService>();
        services.AddSingleton<ITotpService, TotpService>();
        services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();
        services.AddSingleton<SigningKeyProvider>();
        services.AddSingleton<IJwksProvider>(provider => provider.GetRequiredService<SigningKeyProvider>());
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IAuthSessionIssuer, AuthSessionIssuer>();
        services.AddScoped<ILoginThrottler, LoginThrottler>();
        services.AddScoped<IAccessTokenDenylist, AccessTokenDenylist>();

        return services;
    }
}
