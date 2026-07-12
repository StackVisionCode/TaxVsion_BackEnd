using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Infrastructure.Audit;
using TaxVision.Signature.Infrastructure.Consents;
using TaxVision.Signature.Infrastructure.Locking;
using TaxVision.Signature.Infrastructure.Persistence;
using TaxVision.Signature.Infrastructure.Persistence.Queries;
using TaxVision.Signature.Infrastructure.Persistence.Repositories;
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
        services.AddScoped<IFileMetadataRefRepository, FileMetadataRefRepository>();
        services.AddScoped<IUserPermissionsProjectionRepository, UserPermissionsProjectionRepository>();
        services.AddScoped<IAuditSecretFactory, AuditSecretFactory>();
        services.AddSingleton<IRsaKeyProvider, RsaSigningKeyProvider>();
        services.AddSingleton<ISigningTokenService, SigningTokenService>();
        services.AddSingleton<IJtiDenylist, InMemoryJtiDenylist>();
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

        services.AddHttpClient<ISignatureServiceTokenAcquirer, SignatureServiceTokenAcquirer>(
            (sp, http) =>
            {
                var opt =
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceAuthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
            }
        );

        services.AddHttpClient<ISignatureCloudStorageClient, SignatureCloudStorageClient>(
            (sp, http) =>
            {
                var opt =
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CloudStorageClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
            }
        );

        return services;
    }

    private static string NormalizeBaseUrl(string url) => url.EndsWith('/') ? url : url + "/";
}
