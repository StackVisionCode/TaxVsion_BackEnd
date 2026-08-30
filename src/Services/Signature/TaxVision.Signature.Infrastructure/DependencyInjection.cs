using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Minio;
using StackExchange.Redis;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.RateLimiting.Abstractions;
using TaxVision.Signature.Infrastructure.Audit;
using TaxVision.Signature.Infrastructure.Consents;
using TaxVision.Signature.Infrastructure.Locking;
using TaxVision.Signature.Infrastructure.Permissions;
using TaxVision.Signature.Infrastructure.Persistence;
using TaxVision.Signature.Infrastructure.Persistence.Queries;
using TaxVision.Signature.Infrastructure.Persistence.Repositories;
using TaxVision.Signature.Infrastructure.RateLimiting;
using TaxVision.Signature.Infrastructure.Scheduling;
using TaxVision.Signature.Infrastructure.Sealing;
using TaxVision.Signature.Infrastructure.Sealing.Cms;
using TaxVision.Signature.Infrastructure.Sealing.HttpClients;
using TaxVision.Signature.Infrastructure.Sealing.Pades;
using TaxVision.Signature.Infrastructure.Security;
using TaxVision.Signature.Infrastructure.Validation;

namespace TaxVision.Signature.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSignatureInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<SignatureDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<SignatureDbContext>());

        // Cifrado compartido (Encryption:MasterKey) para el secreto HMAC del audit trail.
        services.AddSecretProtection();

        // Repositorios y servicios de dominio auxiliares.
        services.AddScoped<ITenantSignatureSettingsRepository, TenantSignatureSettingsRepository>();
        services.AddScoped<ISignatureRequestRepository, SignatureRequestRepository>();
        // Read service base + decorator con caché distribuida (30s TTL) para el listado
        // del dashboard staff. El decorator resuelve el inner service explícitamente para
        // evitar recursión infinita al pedir ISignatureRequestReadService.
        services.AddScoped<SignatureRequestReadService>();
        services.AddScoped<ISignatureRequestReadService>(sp => new CachedSignatureRequestReadService(
            sp.GetRequiredService<SignatureRequestReadService>(),
            sp.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>()
        ));
        services.AddScoped<ISignatureTemplateRepository, SignatureTemplateRepository>();
        services.AddScoped<ISignatureTemplateReadService, SignatureTemplateReadService>();
        services.AddScoped<ISignatureAnalyticsRepository, SignatureAnalyticsRepository>();
        services.AddScoped<ISignatureAnalyticsReadService, SignatureAnalyticsReadService>();
        services.AddScoped<IDocumentValidationRepository, DocumentValidationRepository>();
        services.AddSingleton<IDocumentValidator, PdfSharpDocumentValidator>();
        services.AddScoped<IConsentEventRepository, ConsentEventRepository>();
        services.AddSingleton<IConsentTextProvider, StaticConsentTextProvider>();
        services.AddScoped<ISignatureAuditRepository, SignatureAuditRepository>();
        services.AddScoped<IAuditChainAppender, HmacAuditChainAppender>();
        services.AddScoped<IAuditChainVerifier, HmacAuditChainVerifier>();

        // CMS signer (BouncyCastle). Registrado sólo si hay certificado configurado —
        // permite arrancar en dev sin PFX. En producción es obligatorio para PAdES-B.
        //
        // PAdES-B ByteRange: PadesBSealer produce firma nativa que Adobe Acrobat valida
        // como "Signature is valid" mediante Signature Dictionary + /ByteRange + /Contents
        // por incremental update byte-level. Requiere PadesCmsSigner + IncrementalSignatureAppender.
        services.AddOptions<CmsSignerOptions>().Bind(configuration.GetSection(CmsSignerOptions.SectionName));
        services.AddOptions<PadesOptions>().Bind(configuration.GetSection(PadesOptions.SectionName));
        var cmsConfigured = !string.IsNullOrWhiteSpace(
            configuration[$"{CmsSignerOptions.SectionName}:CertificatePath"]
        );
        if (cmsConfigured)
        {
            services.AddSingleton<PadesCmsSigner>();
            services.AddSingleton(sp => new IncrementalSignatureAppender(
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PadesOptions>>().Value
            ));
            services.AddSingleton<ICmsPdfSigner, PadesBSealer>();
        }

        // TSA (RFC 3161) para PAdES-B-T. Registrado siempre — el CmsSigner lo consulta
        // opcionalmente. Sin configuración explícita apunta a FreeTSA (dev/testing).
        services.AddOptions<TsaClientOptions>().Bind(configuration.GetSection(TsaClientOptions.SectionName));
        services.AddHttpClient<ITimestampAuthorityClient, FreeTsaClient>();

        // PAdES-B-LT: fetchers de CRL/OCSP + enricher que agrega el DSS al PDF firmado.
        // Cachean por dia (CRL) y 6h (OCSP) usando IDistributedCache (Redis o memoria).
        services.AddHttpClient<CrlFetcher>();
        services.AddHttpClient<OcspFetcher>();
        services.AddSingleton<LongTermValidationEnricher>();

        // Background schedulers (Fases 5 y 9). Se registran siempre; el purge además tiene
        // un feature flag propio (default OFF) para evitar borrar por accidente en dev.
        services.AddHostedService<ExpirationScheduler>();
        // Red de seguridad para la carrera FileAvailable-vs-create: promueve Draft → Ready los
        // borradores cuyo archivo ya está disponible pero se quedaron sin promover.
        services.AddHostedService<ReadyReconciliationScheduler>();
        services.AddHostedService<ReminderScheduler>();
        services.AddOptions<PurgeSchedulerOptions>().Bind(configuration.GetSection(PurgeSchedulerOptions.SectionName));
        services.AddHostedService<PurgeScheduler>();

        // Distributed lock + cache (Redis). Si no hay connection string se degrada a no-op.
        var redisConnectionString = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
            services.AddSingleton<IDistributedLock, RedisDistributedLock>();
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = "signature:";
            });
        }
        else
        {
            services.AddSingleton<IDistributedLock, NoOpDistributedLock>();
            services.AddDistributedMemoryCache();
        }
        services.AddScoped<ICustomerEmailProjectionRepository, CustomerEmailProjectionRepository>();
        services.AddScoped<ITenantBrandingRefRepository, TenantBrandingRefRepository>();
        services.AddScoped<IFileMetadataRefRepository, FileMetadataRefRepository>();
        services.AddScoped<ISignerRoleAuditSnapshotRepository, SignerRoleAuditSnapshotRepository>();
        // RBAC Fase 7 (RBAC_Hardening_Plan.md) -- proyeccion local de permisos de AUTORIZACION
        // consultada por ProjectionPermissionsSource cuando Authorization:PermissionsSource=
        // "Projection". Distinta de ISignerRoleAuditSnapshotRepository de arriba (esa alimenta
        // la proyeccion de auditoria, no de autorizacion). La misma instancia scoped
        // satisface el puerto local rico (para los consumers) y el puerto compartido y angosto de
        // BuildingBlocks (para la autorizacion), evitando dos lecturas separadas del mismo dato.
        services.AddScoped<AuthzUserPermissionsProjectionRepository>();
        services.AddScoped<IAuthzUserPermissionsProjectionRepository>(sp =>
            sp.GetRequiredService<AuthzUserPermissionsProjectionRepository>()
        );
        services.AddScoped<BuildingBlocks.Permissions.IUserPermissionsProjectionReader>(sp =>
            sp.GetRequiredService<AuthzUserPermissionsProjectionRepository>()
        );
        services.AddScoped<IAuthzRolePermissionsProjectionRepository, AuthzRolePermissionsProjectionRepository>();
        services.AddScoped<IAuditSecretFactory, AuditSecretFactory>();
        services.AddSingleton<IRsaKeyProvider, RsaSigningKeyProvider>();
        services.AddSingleton<ISigningTokenService, SigningTokenService>();
        // Denylist de jti distribuida (Redis vía ICacheService), para que una revocación por-token
        // valga en toda la flota. Scoped como el resto de lectores que dependen de ICacheService.
        services.AddScoped<IJtiDenylist, CachedJtiDenylist>();
        services.AddSingleton<IPinHasher, Pbkdf2PinHasher>();
        services.AddSingleton<IOtpCodeGenerator, CryptoOtpCodeGenerator>();

        // Sealing worker: engines puros (singleton) + HTTP clients con token M2M.
        // PdfSharp 6.x requires an explicit IFontResolver — register once, process-wide.
        if (PdfSharp.Fonts.GlobalFontSettings.FontResolver is null)
            PdfSharp.Fonts.GlobalFontSettings.FontResolver = new SealingFontResolver();
        services.AddSingleton<IDocumentSealingEngine, PdfSharpSealingEngine>();
        services.AddSingleton<ICertificateOfCompletionRenderer, PdfSharpCertificateRenderer>();

        services
            .AddOptions<ServiceAuthClientOptions>()
            .Bind(configuration.GetSection(ServiceAuthClientOptions.SectionName));
        services
            .AddOptions<CloudStorageClientOptions>()
            .Bind(configuration.GetSection(CloudStorageClientOptions.SectionName));
        services.AddOptions<CustomerClientOptions>().Bind(configuration.GetSection(CustomerClientOptions.SectionName));
        services.AddOptions<SignatureMinioOptions>().Bind(configuration.GetSection(SignatureMinioOptions.SectionName));

        services.AddHttpClient<ISignatureServiceTokenAcquirer, SignatureServiceTokenAcquirer>(
            (sp, http) =>
            {
                var opt =
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceAuthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
            }
        );

        AddRateLimitTierQuotas(services, configuration);

        // Fase D1 — cliente MinIO propio de Signature, credenciales scoped (IAM
        // signature-source, ver deploy/docker/minio/policies/signature-source.json),
        // nunca las root de CloudStorage. Solo para el UploadAsync del sellado; el
        // DownloadAsync del original sigue via el HttpClient de abajo.
        services.AddSingleton<Minio.IMinioClient>(sp =>
        {
            var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SignatureMinioOptions>>().Value;
            var builder = new Minio.MinioClient()
                .WithEndpoint(opt.Endpoint)
                .WithCredentials(opt.AccessKey, opt.SecretKey);
            if (opt.UseTls)
                builder = builder.WithSSL();
            return builder.Build();
        });

        services.AddHttpClient<ISignatureCloudStorageClient, SignatureCloudStorageClient>(
            (sp, http) =>
            {
                var opt =
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CloudStorageClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
            }
        );

        // Auto-reparación de la proyección CustomerEmailProjection: cliente M2M al endpoint global de
        // reconciliación de Customer + job periódico. Reusa el IServiceTokenAcquirer compartido (pide el
        // token para PlatformTenant, única identidad que el gate del endpoint acepta).
        services.AddHttpClient<ICustomerReconciliationClient, Reconciliation.SignatureCustomerReconciliationClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CustomerClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );
        services.AddHostedService<CustomerProjectionReconciliationJob>();

        AddPermissionsPullRecovery(services);
        return services;
    }

    // RateLimit Fase 2 — piezas siempre registradas: el consumer del evento de Subscription
    // (mantiene la proyección al día incluso con el flag apagado) y los lectores concretos. El
    // mapeo a ITenantPlanCodeReader/IPlanRateLimitReader (los que RateLimitQuotaResolver
    // realmente consume) es condicional al flag RateLimit:EnforceTierQuotas — decidido en
    // Program.cs, ANTES de AddTieredRateLimiting().
    private static void AddRateLimitTierQuotas(IServiceCollection services, IConfiguration configuration)
    {
        // HttpPlanRateLimitReader (BuildingBlocks.Infrastructure.RateLimiting) depende del
        // contrato compartido; SignatureServiceTokenAcquirer ya lo implementa (F25 + Fase 2), solo
        // falta el forwarding.
        services.AddTransient<BuildingBlocks.Infrastructure.Security.IServiceTokenAcquirer>(sp =>
            (BuildingBlocks.Infrastructure.Security.IServiceTokenAcquirer)
                sp.GetRequiredService<ISignatureServiceTokenAcquirer>()
        );

        services.AddScoped<ITenantPlanCodeProjectionRepository, TenantPlanCodeProjectionRepository>();
        services.AddScoped<EfTenantPlanCodeReader>();
        services.AddScoped<CachedTenantPlanCodeReader>(sp => new CachedTenantPlanCodeReader(
            sp.GetRequiredService<BuildingBlocks.Caching.ICacheService>(),
            sp.GetRequiredService<EfTenantPlanCodeReader>()
        ));
        services.AddScoped<
            BuildingBlocks.RateLimiting.ITenantPlanCodeCacheInvalidator,
            TenantPlanCodeCacheInvalidator
        >();

        services
            .AddOptions<SubscriptionClientOptions>()
            .Bind(configuration.GetSection(SubscriptionClientOptions.SectionName));
        services.AddHttpClient<HttpPlanRateLimitReader>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<SubscriptionClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );
    }

    // H-04 — recuperación pull bajo demanda de permisos. Cuando ProjectionPermissionsSource
    // (BuildingBlocks.Web) no encuentra la fila local, pregunta a Auth en vez de negar sin más:
    // evento perdido, backfill pendiente o usuario recién creado dejan de ser un 403 permanente.
    // Reutiliza el IServiceTokenAcquirer y el ServiceAuthClientOptions que ya apuntan a Auth.
    private static void AddPermissionsPullRecovery(IServiceCollection services)
    {
        services.AddScoped<IUserPermissionsProjectionWriter, PermissionsProjectionWriter>();
        services.AddHttpClient<IPermissionsSnapshotClient, PermissionsSnapshotClient>(
            (sp, http) =>
            {
                var options = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(options.AuthBaseUrl));
                http.Timeout = TimeSpan.FromSeconds(15);
            }
        );
    }

    private static string NormalizeBaseUrl(string url) => url.EndsWith('/') ? url : url + "/";
}
