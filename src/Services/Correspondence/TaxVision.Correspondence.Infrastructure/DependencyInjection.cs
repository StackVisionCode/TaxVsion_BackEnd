using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Minio;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Backfill;
using TaxVision.Correspondence.Application.Ingest;
using TaxVision.Correspondence.Application.Reconciliation;
using TaxVision.Correspondence.Infrastructure.CloudStorage;
using TaxVision.Correspondence.Infrastructure.Connectors;
using TaxVision.Correspondence.Infrastructure.Customers;
using TaxVision.Correspondence.Infrastructure.Jobs;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;
using TaxVision.Correspondence.Infrastructure.Postmaster;

namespace TaxVision.Correspondence.Infrastructure;

/// <summary>
/// Fase 2 agrega el primer repositorio real (CustomerEmailAddresses) más el backfill de
/// clientes preexistentes (TenantBackfillStates + cliente M2M a Customer.Api). Las fases
/// siguientes agregan más clientes HTTP, rate limiters, etc. a medida que exista dominio
/// real (mismo patron que Connectors arranco en su propia Fase 1).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCorrespondenceInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<CorrespondenceDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<CorrespondenceDbContext>());

        services.AddScoped<ICustomerEmailAddressRepository, CustomerEmailAddressRepository>();
        services.AddScoped<ITenantBackfillStateRepository, TenantBackfillStateRepository>();
        services.AddScoped<ITenantCustomerBackfillService, TenantCustomerBackfillService>();
        // Fase 16 — reconciliación periódica, reusa ICorrespondenceCustomerClient del backfill de arriba.
        services.AddScoped<ICustomerEmailReconciliationService, CustomerEmailReconciliationService>();
        services.AddScoped<IIncomingEmailRepository, IncomingEmailRepository>();
        services.AddScoped<IEmailThreadRepository, EmailThreadRepository>();
        services.AddScoped<IUnmatchedIncomingEmailRepository, UnmatchedIncomingEmailRepository>();
        // Fase 10 — compose/reply.
        services.AddScoped<IDraftRepository, DraftRepository>();
        // Fase 14 — rastro mínimo de auditoría (solo escritura, ver ICorrespondenceAuditLogRepository).
        services.AddScoped<ICorrespondenceAuditLogRepository, CorrespondenceAuditLogRepository>();
        // Fase 6 — resuelve threading (4 capas) para RawMessageReceivedConsumer; Wolverine lo
        // inyecta como cualquier otro parámetro de Handle, igual que los repositorios de arriba.
        services.AddScoped<ThreadResolver>();

        services
            .AddOptions<ServiceAuthClientOptions>()
            .Bind(configuration.GetSection(ServiceAuthClientOptions.SectionName));
        services.AddOptions<CustomerClientOptions>().Bind(configuration.GetSection(CustomerClientOptions.SectionName));
        services
            .AddOptions<ConnectorsClientOptions>()
            .Bind(configuration.GetSection(ConnectorsClientOptions.SectionName));
        services
            .AddOptions<CorrespondenceIngestOptions>()
            .Bind(configuration.GetSection(CorrespondenceIngestOptions.SectionName));
        services
            .AddOptions<CorrespondenceMinioOptions>()
            .Bind(configuration.GetSection(CorrespondenceMinioOptions.SectionName));
        services
            .AddOptions<CloudStorageClientOptions>()
            .Bind(configuration.GetSection(CloudStorageClientOptions.SectionName));
        services
            .AddOptions<PostmasterClientOptions>()
            .Bind(configuration.GetSection(PostmasterClientOptions.SectionName));
        services.AddOptions<DraftCleanupOptions>().Bind(configuration.GetSection(DraftCleanupOptions.SectionName));
        services
            .AddOptions<CustomerEmailReconciliationOptions>()
            .Bind(configuration.GetSection(CustomerEmailReconciliationOptions.SectionName));

        // Fase 1 (hardening) — timeout 30s fijo. No estaba en el alcance textual de la Fase 1 del
        // plan (que solo nombra CorrespondenceCustomerClient/CloudStorageClient), pero es el mismo
        // gap de la misma clase en el mismo archivo: sin este cap cae al default de 100s del
        // framework, igual que PostmasterServiceTokenAcquirer ya lo tiene fijo (Postmaster
        // DependencyInjection.cs) — se corrige acá para no dejar la mitad del patrón sin aplicar.
        services.AddHttpClient<ICorrespondenceServiceTokenAcquirer, CorrespondenceServiceTokenAcquirer>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        // Fase 1 (hardening) — timeout 30s fijo, mismo valor que ConnectorsClient/PostmasterClient
        // más abajo: este cliente se llama desde dentro de cada consumer de eventos de Customer
        // (backfill reactivo), sin cap podia retener capacidad del listener hasta 100s por mensaje.
        services.AddHttpClient<ICorrespondenceCustomerClient, CorrespondenceCustomerClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<CustomerClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        // Fase 5 — body fetch bajo demanda. Timeout 30s fijo en el HttpClient (mismo total que
        // Connectors ya se impone a sí mismo internamente contra el proveedor, ver
        // GetMessageBodyHandler.FetchTimeout); el retry 1x vive en ConnectorsClient, no acá.
        services.AddHttpClient<IConnectorsClient, ConnectorsClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<ConnectorsClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        // Fase 8 — cliente MinIO propio de Correspondence, credenciales scoped (IAM
        // correspondence-source, ver deploy/docker/minio/policies/correspondence-source.json),
        // nunca las root de CloudStorage. Mismo patrón D0/D1 que Signature.
        services.AddSingleton<IMinioClient>(sp =>
        {
            var opt = sp.GetRequiredService<IOptions<CorrespondenceMinioOptions>>().Value;
            var builder = new MinioClient().WithEndpoint(opt.Endpoint).WithCredentials(opt.AccessKey, opt.SecretKey);
            if (opt.UseTls)
                builder = builder.WithSSL();
            return builder.Build();
        });
        services.AddScoped<ICorrespondenceTempBucketUploader, CorrespondenceTempBucketUploader>();

        // Fase 8 — obtiene la URL presignada del attachment ya subido/escaneado por CloudStorage.
        // Fase 1 (hardening) — timeout 30s fijo: se llama desde un request de usuario en vivo
        // (adjuntar archivo al draft, pedir signed URL), mismo valor que el resto de este archivo.
        services.AddHttpClient<ICloudStorageClient, CloudStorageClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<CloudStorageClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        // Fase 14 — cierre síncrono de la cadena de envío. Timeout 30s fijo (la request HTTP del
        // usuario que apretó "Enviar" está bloqueada esperando esto en vivo); sin retry — ver el
        // comentario de clase de PostmasterClient sobre por qué (idempotencia ya vive en Postmaster).
        services.AddHttpClient<IPostmasterClient, PostmasterClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<PostmasterClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        // Fase 16 — jobs de hardening final: DraftCleanupJob (plan §30, deshabilitado por default)
        // y CustomerEmailReconciliationJob (plan §32 R1, habilitado por default).
        services.AddHostedService<DraftCleanupJob>();
        services.AddHostedService<CustomerEmailReconciliationJob>();

        return services;
    }

    private static string NormalizeBaseUrl(string url) => url.EndsWith('/') ? url : url + "/";
}
