using System.Net.Http.Headers;
using BuildingBlocks.Persistence;
using BuildingBlocks.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Invitations.Commands;
using TaxVision.Auth.Application.Onboarding;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.ServiceTokens;
using TaxVision.Auth.Application.TenantDomains;
using TaxVision.Auth.Application.Terms;
using TaxVision.Auth.Infrastructure.Cloudflare;
using TaxVision.Auth.Infrastructure.Onboarding.HttpClients;
using TaxVision.Auth.Infrastructure.Onboarding.Observability;
using TaxVision.Auth.Infrastructure.Onboarding.Persistence.Repositories;
using TaxVision.Auth.Infrastructure.Onboarding.RateLimit;
using TaxVision.Auth.Infrastructure.Onboarding.Security;
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
        services.Configure<OnboardingOptions>(configuration.GetSection(OnboardingOptions.SectionName));
        services
            .AddOptions<PaymentAppClientOptions>()
            .Bind(configuration.GetSection(PaymentAppClientOptions.SectionName));
        services
            .AddOptions<DocumentsClientOptions>()
            .Bind(configuration.GetSection(DocumentsClientOptions.SectionName));
        services
            .AddOptions<CloudStorageClientOptions>()
            .Bind(configuration.GetSection(CloudStorageClientOptions.SectionName));

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
        services.AddScoped<IEmailVerificationChallengeRepository, EmailVerificationChallengeRepository>();
        services.AddScoped<ITermsVersionRepository, TermsVersionRepository>();
        services.AddScoped<ITenantOnboardingRepository, TenantOnboardingRepository>();
        services.AddScoped<IOnboardingSubdomainReservationRepository, OnboardingSubdomainReservationRepository>();
        services.AddOptions<TenantClientOptions>().Bind(configuration.GetSection(TenantClientOptions.SectionName));
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
        services.AddSingleton<IOnboardingMetrics, OnboardingMetrics>();
        services.AddSingleton<ISecureTokenService, SecureTokenService>();
        services.AddSingleton<ITotpService, TotpService>();
        services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();
        services.AddSingleton<SigningKeyProvider>();
        services.AddSingleton<IJwksProvider>(provider => provider.GetRequiredService<SigningKeyProvider>());
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IAuthSessionIssuer, AuthSessionIssuer>();
        services.AddScoped<ILoginThrottler, LoginThrottler>();
        // RBAC Fase 6 — un solo AccessTokenDenylist por scope resuelve ambas interfaces: escritura
        // (IAccessTokenDenylist, revocación) y lectura (ISessionDenylistReader, consumida por el
        // SessionDenylistMiddleware compartido de BuildingBlocks.Web).
        services.AddScoped<AccessTokenDenylist>();
        services.AddScoped<IAccessTokenDenylist>(sp => sp.GetRequiredService<AccessTokenDenylist>());
        services.AddScoped<ISessionDenylistReader>(sp => sp.GetRequiredService<AccessTokenDenylist>());

        // Onboarding (PayFlow) — Fase 5
        services.AddSingleton<IOtpCodeGenerator, NumericOtpCodeGenerator>();
        services.AddScoped<IOnboardingOtpThrottler, RedisOnboardingOtpThrottler>();

        // Onboarding (PayFlow) — Fase 6
        services.AddHttpClient<ITermsDocumentHasher, HttpTermsDocumentHasher>(client =>
            client.Timeout = TimeSpan.FromSeconds(30)
        );

        // Onboarding (PayFlow) — Fase 9. Auth ya asume Redis disponible sin fallback (ver
        // AddRedisCache/AddSessionDenylist más arriba) — primer uso de IConnectionMultiplexer
        // crudo en Auth, necesario para el GETDEL atómico de RedisTokenReferenceStore.
        var redisConnectionString = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
        services.AddScoped<ITokenReferenceStore, RedisTokenReferenceStore>();
        services.AddHttpClient<IPaymentAppOnboardingClient, PaymentAppOnboardingClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<PaymentAppClientOptions>>().Value;
                http.BaseAddress = new Uri(opt.BaseUrl.EndsWith('/') ? opt.BaseUrl : opt.BaseUrl + "/");
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        // Onboarding (PayFlow) — Fase 11
        services.AddHttpClient<IReceiptDocumentClient, ReceiptDocumentClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<DocumentsClientOptions>>().Value;
                http.BaseAddress = new Uri(opt.BaseUrl.EndsWith('/') ? opt.BaseUrl : opt.BaseUrl + "/");
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );
        services.AddHttpClient<ICloudStorageDownloadUrlClient, CloudStorageDownloadUrlClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<CloudStorageClientOptions>>().Value;
                http.BaseAddress = new Uri(opt.BaseUrl.EndsWith('/') ? opt.BaseUrl : opt.BaseUrl + "/");
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        // Onboarding (PayFlow) — Fase 14
        services.AddHttpClient<ITenantSubdomainAvailabilityClient, TenantSubdomainAvailabilityClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<TenantClientOptions>>().Value;
                http.BaseAddress = new Uri(opt.BaseUrl.EndsWith('/') ? opt.BaseUrl : opt.BaseUrl + "/");
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        // Onboarding (PayFlow) — Fase 15: Saga y sus 3 clientes M2M (Tenant reusa TenantClientOptions
        // de Fase 14; el loopback a Auth reusa OnboardingOptions.AuthPublicBaseUrl de Fase 11/13;
        // Subscription es el único cliente de esta fase con options nuevas).
        services
            .AddOptions<SubscriptionClientOptions>()
            .Bind(configuration.GetSection(SubscriptionClientOptions.SectionName));

        services.AddHttpClient<ITenantProvisioningClient, TenantProvisioningClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<TenantClientOptions>>().Value;
                http.BaseAddress = new Uri(opt.BaseUrl.EndsWith('/') ? opt.BaseUrl : opt.BaseUrl + "/");
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        services.AddHttpClient<IAuthInternalOwnerCreationClient, AuthInternalOwnerCreationClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<OnboardingOptions>>().Value;
                var baseUrl = opt.AuthPublicBaseUrl;
                http.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        services.AddHttpClient<ISubscriptionActivationClient, SubscriptionActivationClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<SubscriptionClientOptions>>().Value;
                http.BaseAddress = new Uri(opt.BaseUrl.EndsWith('/') ? opt.BaseUrl : opt.BaseUrl + "/");
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        return services;
    }
}
