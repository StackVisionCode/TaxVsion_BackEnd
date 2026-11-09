using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Infrastructure.Resilience;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using BuildingBlocks.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Minio;
using StackExchange.Redis;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Invitations.Commands;
using TaxVision.Auth.Application.Offboarding;
using TaxVision.Auth.Application.RateLimiting.Abstractions;
using TaxVision.Auth.Application.ServiceTokens;
using TaxVision.Auth.Application.SlotGating.Abstractions;
using TaxVision.Auth.Application.TenantDomains;
using TaxVision.Auth.Application.Terms;
using TaxVision.Auth.Infrastructure.Cloudflare;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Infrastructure.RateLimiting;
using TaxVision.Auth.Infrastructure.Security;
using TaxVision.Auth.Infrastructure.SlotGating;
using TaxVision.Auth.Infrastructure.Storage;
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
        services.Configure<MfaOptions>(configuration.GetSection(MfaOptions.SectionName));
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
        services.AddScoped<
            TaxVision.Auth.Application.SlotGating.Abstractions.ITenantSeatSlotProjectionRepository,
            TenantSeatSlotProjectionRepository
        >();
        services.AddScoped<ISeatSlotRecomputeSerializer, SqlSeatSlotRecomputeSerializer>();
        services.AddScoped<ITenantDomainRepository, TenantDomainRepository>();
        services.AddScoped<ITenantSubdomainReservationRepository, TenantSubdomainReservationRepository>();
        services.AddScoped<ITenantResolutionCache, TenantResolutionCache>();
        services.AddScoped<ITenantResolver, TenantResolver>();
        services.AddScoped<ITenantDomainRebaser, TenantDomainRebaser>();
        services.AddScoped<ITenantTermsAcceptanceRepository, TenantTermsAcceptanceRepository>();

        // Regla del último admin: candado por oficina dentro de la transacción del handler.
        services.Configure<TenantAdminLockOptions>(configuration.GetSection(TenantAdminLockOptions.SectionName));
        services.AddScoped<ITenantAdminLock, SqlTenantAdminLock>();
        services.AddScoped<IUserLoginLock, SqlUserLoginLock>();

        // Acuse por servicio del retiro: corridas, try-lock del barrido y opciones validadas al arrancar.
        services.AddScoped<IOffboardingRunRepository, OffboardingRunRepository>();
        services.AddScoped<IOffboardingSweepLock, SqlOffboardingSweepLock>();
        services.AddScoped<OffboardingRunSweeper>();
        services
            .AddOptions<OffboardingTrackingOptions>()
            .Bind(configuration.GetSection(OffboardingTrackingOptions.SectionName))
            .Validate(o => !o.Errors().Any(), "Offboarding:Tracking is invalid (see OffboardingTrackingOptions).")
            .ValidateOnStart();
        services.AddScoped<ITermsVersionRepository, TermsVersionRepository>();
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
        services.AddSingleton<
            TaxVision.Auth.Application.Subscriptions.Abstractions.ISubscriptionAccessMetrics,
            TaxVision.Auth.Infrastructure.Subscriptions.Observability.SubscriptionAccessMetrics
        >();
        services.AddSingleton<ISecureTokenService, SecureTokenService>();
        services.AddSingleton<ITotpService, TotpService>();
        // BB-10 — el protector es el compartido de BuildingBlocks, pero con la clave de Auth: los
        // secretos TOTP están cifrados con Mfa:EncryptionKey y pasarlos a Encryption:MasterKey los
        // volvería ilegibles (todo usuario con MFA perdería su segundo factor).
        services.AddSingleton<ISecretProtector>(_ => new AesGcmSecretProtector(ResolveMfaKey(configuration)));
        // NOT-04: token protegido con la clave compartida con Notification; sin ella los eventos van solo con RawToken.
        if (!string.IsNullOrWhiteSpace(configuration["Encryption:MasterKey"]))
            services.AddProtectedPayloadProtection(configuration);
        services.AddSingleton<IAccountTokenProtector>(provider => new AccountTokenProtector(
            provider.GetService<BuildingBlocks.Messaging.ClientFormsIntegrationEvents.IProtectedPayloadCodec>()
        ));
        services.AddSingleton<SigningKeyProvider>();
        services.AddSingleton<IJwksProvider>(provider => provider.GetRequiredService<SigningKeyProvider>());
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IAuthSessionIssuer, AuthSessionIssuer>();
        services.AddScoped<ILoginThrottler, LoginThrottler>();

        // AUTH-7: colaboradores de login y retiro. Ninguno recibe IMessageBus ni guarda: eso es del handler.
        services.AddScoped<TaxVision.Auth.Application.Common.AuthAuditTrail>();
        services.AddScoped<TaxVision.Auth.Application.Common.SessionAccessCutoff>();
        services.AddScoped<TaxVision.Auth.Application.Offboarding.OffboardingHygiene>();
        services.AddScoped<TaxVision.Auth.Application.Users.Login.LoginDiscovery>();
        services.AddScoped<TaxVision.Auth.Application.Users.Login.LoginPasswordCheck>();
        services.AddScoped<TaxVision.Auth.Application.Users.Login.LoginMfaStep>();
        services.AddScoped<TaxVision.Auth.Application.Users.Login.LoginSessionStep>();
        // RBAC Fase 6 — un solo AccessTokenDenylist por scope resuelve ambas interfaces: escritura
        // (IAccessTokenDenylist, revocación) y lectura (ISessionDenylistReader, consumida por el
        // SessionDenylistMiddleware compartido de BuildingBlocks.Web).
        services.AddScoped<AccessTokenDenylist>();
        services.AddScoped<IAccessTokenDenylist>(sp => sp.GetRequiredService<AccessTokenDenylist>());
        services.AddScoped<ISessionDenylistReader>(sp => sp.GetRequiredService<AccessTokenDenylist>());

        // Auditoría (gap MinIO/legal-docs) — credenciales MinIO propias de Auth (primer uso en el
        // servicio) para subir documentos legales (ToS/Privacy Policy), patrón D0/D1 igual que
        // Documents/Scribe. Nunca las credenciales root de CloudStorage.
        services.AddOptions<AuthMinioOptions>().Bind(configuration.GetSection(AuthMinioOptions.SectionName));
        services.AddSingleton<IMinioClient>(sp =>
        {
            var opt = sp.GetRequiredService<IOptions<AuthMinioOptions>>().Value;
            var builder = new MinioClient().WithEndpoint(opt.Endpoint).WithCredentials(opt.AccessKey, opt.SecretKey);
            if (opt.UseTls)
                builder = builder.WithSSL();
            return builder.Build();
        });
        // Fotos de perfil del staff: misma cuenta IAM y bucket temporal, prefijo {SourcePrefix}/avatars.
        services.AddScoped<IUserAvatarStorage, TaxVision.Auth.Infrastructure.Storage.UserAvatarStorage>();
        services.AddHttpClient<ITermsContentStorageClient, TermsContentStorageClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<CloudStorageClientOptions>>().Value;
                http.BaseAddress = new Uri(opt.BaseUrl.EndsWith('/') ? opt.BaseUrl : opt.BaseUrl + "/");
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        // Auth ya asume Redis disponible sin fallback (ver AddRedisCache/AddSessionDenylist más arriba):
        // IConnectionMultiplexer crudo, necesario para el GETDEL atómico de RedisTokenReferenceStore.
        var redisConnectionString = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
        services.AddScoped<ITokenReferenceStore, RedisTokenReferenceStore>();
        // Login central (Opción A): el vale de handoff cross-dominio y la sesión de descubrimiento,
        // misma dependencia de Redis crudo.
        services.AddScoped<IHandoffTicketStore, RedisHandoffTicketStore>();
        services.AddScoped<IDiscoverySessionStore, RedisDiscoverySessionStore>();
        services.AddScoped<ISessionRevocationPublisher, RedisSessionRevocationPublisher>();
        services.AddScoped<ISessionTakeoverTicketStore, RedisSessionTakeoverTicketStore>();
        // Vale del botón "Manage subscription" del CRM hacia el Account del Landing (mismo sid).
        services.AddScoped<IAccountHandoffTicketStore, RedisAccountHandoffTicketStore>();

        // Rate Limit Fase 0.1 — contador atómico compartido entre réplicas para LoginThrottler
        // (antes GET+SET no atómico sobre ICacheService, ver doc-comment de LoginThrottler.cs).
        services.AddSingleton<IRateCounter, RedisRateCounter>();

        // Cache singleton de tokens M2M por clientId, ver ServiceTokenCache.
        services.AddSingleton<ServiceTokenCache>();

        AddRateLimitTierQuotas(services, configuration);

        return services;
    }

    // RateLimit Fase 2 — piezas siempre registradas: el consumer del evento de Subscription
    // (TenantPlanCodeProjectionConsumer, mantiene la proyección al día incluso con el flag
    // apagado) y los lectores concretos de la proyección local, más HttpPlanRateLimitReader (el
    // catálogo global de PlanRateLimits). El mapeo a ITenantPlanCodeReader/IPlanRateLimitReader
    // (los que RateLimitQuotaResolver realmente consume) es condicional al flag
    // RateLimit:EnforceTierQuotas — decidido en Program.cs, ANTES de AddTieredRateLimiting(),
    // igual que Customer/Tenant.
    //
    // A diferencia de CloudStorage (que nunca tuvo un IServiceTokenAcquirer M2M propio porque es
    // un servicio de recursos al que los demás llaman, no un llamador, y por eso solo wireó la
    // mitad local de este mecanismo), Auth SÍ tiene un mecanismo M2M saliente propio desde PayFlow
    // Fase 13/25: ServiceTokenCache mintea tokens de servicio en proceso (sin HTTP hacia
    // sí mismo) para llamar a Subscription/PaymentApp/Tenant/Documents/CloudStorage. Auth es la
    // fuente de los tokens M2M, no necesita pedirle uno a sí mismo por HTTP — por eso
    // AuthServiceTokenAcquirer adapta ese mecanismo existente a IServiceTokenAcquirer en vez de
    // inventar un cliente HTTP nuevo, y por eso acá SÍ se wirea el par completo
    // (ITenantPlanCodeReader + IPlanRateLimitReader), a diferencia de CloudStorage/Connectors.
    private static void AddRateLimitTierQuotas(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<BuildingBlocks.Infrastructure.Security.IServiceTokenAcquirer, AuthServiceTokenAcquirer>();

        services.AddScoped<ITenantPlanCodeProjectionRepository, TenantPlanCodeProjectionRepository>();
        services.AddScoped<EfTenantPlanCodeReader>();
        services.AddScoped<BuildingBlocks.Infrastructure.RateLimiting.CachedTenantPlanCodeReader>(
            sp => new BuildingBlocks.Infrastructure.RateLimiting.CachedTenantPlanCodeReader(
                sp.GetRequiredService<BuildingBlocks.Caching.ICacheService>(),
                sp.GetRequiredService<EfTenantPlanCodeReader>()
            )
        );
        services.AddScoped<
            BuildingBlocks.RateLimiting.ITenantPlanCodeCacheInvalidator,
            TenantPlanCodeCacheInvalidator
        >();

        // Tipo compartido de BuildingBlocks (sección "SubscriptionClient") que HttpPlanRateLimitReader
        // exige por firma. El de Onboarding, con el mismo nombre, usa la sección "Auth:Subscription".
        services
            .AddOptions<BuildingBlocks.Infrastructure.RateLimiting.SubscriptionClientOptions>()
            .Bind(config.GetSection(BuildingBlocks.Infrastructure.RateLimiting.SubscriptionClientOptions.SectionName));
        services.AddHttpClient<BuildingBlocks.Infrastructure.RateLimiting.HttpPlanRateLimitReader>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<
                    IOptions<BuildingBlocks.Infrastructure.RateLimiting.SubscriptionClientOptions>
                >().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
            }
        );
    }

    /// <summary>
    /// Clave AES-256 de los secretos TOTP. Preferencia: <c>Mfa:EncryptionKey</c> (base64, 32 bytes).
    /// Si falta, se deriva de <c>Jwt:Secret</c> — aceptable en desarrollo, pero en producción hay que
    /// configurarla: rotar el secreto JWT dejaría ilegibles todos los secretos MFA ya guardados.
    /// </summary>
    private static byte[] ResolveMfaKey(IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration["Mfa:EncryptionKey"]))
            return AesGcmSecretProtector.ResolveKey(configuration, "Mfa:EncryptionKey");

        var jwtSecret =
            configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Mfa:EncryptionKey or Jwt:Secret must be configured.");

        return SHA256.HashData(Encoding.UTF8.GetBytes($"{jwtSecret}:taxvision-mfa"));
    }

    private static string NormalizeBaseUrl(string url) => url.EndsWith('/') ? url : url + "/";
}
