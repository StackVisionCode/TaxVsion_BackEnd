using System.Text.Json.Serialization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Health;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Middleware;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Infrastructure;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Security;
using TaxVision.CloudStorage.Infrastructure.Storage;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseTaxVisionSerilog("cloudstorage-service");
builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

builder.Services.AddBuildingBlocks();
builder.Services.AddCloudStorageInfrastructure(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "cloudstorage-service");
builder.Services.AddAuthorization(options =>
{
    foreach (
        var permission in new[]
        {
            CloudStoragePermissions.FileView,
            CloudStoragePermissions.FileUpload,
            CloudStoragePermissions.FileDownload,
            CloudStoragePermissions.FileDelete,
            CloudStoragePermissions.SettingsManage,
            CloudStoragePermissions.AuditView,
        }
    )
        options.AddPolicy(permission, policy => policy.RequireClaim("perm", permission));
});

var rabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);
var minio = builder.Configuration.GetSection(MinioOptions.SectionName).Get<MinioOptions>() ?? new MinioOptions();
var clamAv = builder.Configuration.GetSection(ClamAvOptions.SectionName).Get<ClamAvOptions>() ?? new ClamAvOptions();
var minioEndpoint = HostPort.Parse(minio.Endpoint, minio.UseTls ? 443 : 9000);

builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<CloudStorageDbContext>("sql-server", tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(rabbitUri.Host, rabbitUri.Port), tags: ["ready"])
    .AddCheck("minio", new TcpEndpointHealthCheck(minioEndpoint.Host, minioEndpoint.Port), tags: ["ready"])
    .AddCheck("clamav", new TcpEndpointHealthCheck(clamAv.Host, clamAv.Port), tags: ["ready"]);

builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(typeof(InitiateUploadHandler).Assembly);
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var sqlConnection =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseRabbitMq(rabbitUri).AutoProvision();
    options.PersistMessagesWithSqlServer(sqlConnection);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, CloudStorageDbContext>();
    options.Policies.AutoApplyTransactions();

    options.PublishMessage<FileAvailableIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<FileInfectedDetectedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<FileDeletedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<StorageLimitExceededIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<FileAccessAuditedIntegrationEvent>().ToRabbitExchange("taxvision-events");

    options
        .ListenToRabbitQueue(
            "cloudstorage-events",
            queue =>
            {
                queue.BindExchange("taxvision-events", string.Empty);
            }
        )
        .UseDurableInbox();

    options
        .Policies.OnException<Exception>()
        .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "CloudStorage API v1"));
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantResolutionMiddleware>();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapControllers();
app.Run();

public partial class Program;
