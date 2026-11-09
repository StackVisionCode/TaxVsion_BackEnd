using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using JasperFx.CodeGeneration.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Email.Templates.Commands;
using TaxVision.Notification.Domain.Emailing;
using TaxVision.Notification.Domain.Emailing.Templates;
using TaxVision.Notification.Infrastructure.Persistence;
using TaxVision.Notification.Infrastructure.Persistence.Repositories;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;

namespace TaxVision.Notification.Tests.Integration;

/// <summary>
/// NOT-14 por el camino real: con la transacción de Wolverine de Program.cs, un publish rechazado no deja
/// versiones cambiadas en la base.
/// </summary>
public sealed class PublishEmailTemplateIntegrationTests : IAsyncLifetime
{
    private readonly NotificationTempDatabase _database = new("notn14_");
    private readonly Guid _tenantId = Guid.NewGuid();
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        _host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<NotificationDbContext>(o => o.UseSqlServer(_database.ConnectionString));
                services.AddScoped<IUnitOfWork>(p => p.GetRequiredService<NotificationDbContext>());
                services.AddSingleton<ITenantContext>(new NotificationTempDatabase.NoTenant());
                services.AddScoped<IEmailTemplateRepository, EmailTemplateRepository>();
            })
            .UseWolverine(options =>
            {
                options.Discovery.DisableConventionalDiscovery().IncludeType(typeof(PublishEmailTemplateHandler));
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Durability.Mode = DurabilityMode.Solo;
                options.PersistMessagesWithSqlServer(_database.ConnectionString, "wolverine_tint");
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, NotificationDbContext>();
                options.Policies.AutoApplyTransactions();
                options.StubAllExternalTransports();
            })
            .StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task Rejected_publish_leaves_the_versions_untouched()
    {
        var (templateId, publishedId, draftId) = await SeedAsync();
        Assert.True((await PublishAsync(templateId, publishedId)).IsSuccess);
        await ArchiveAsync(templateId);

        var result = await PublishAsync(templateId, draftId);

        Assert.True(result.IsFailure);
        await using var check = _database.NewContext();
        var versions = await check.EmailTemplateVersions.Where(v => v.TemplateId == templateId).ToListAsync();
        Assert.Equal(EmailTemplateVersionStatus.Published, versions.Single(v => v.Id == publishedId).Status);
        Assert.Equal(EmailTemplateVersionStatus.Draft, versions.Single(v => v.Id == draftId).Status);
    }

    private async Task<Result> PublishAsync(Guid templateId, Guid versionId) =>
        await _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result>(new PublishEmailTemplateCommand(templateId, versionId, _tenantId, false));

    private async Task<(Guid TemplateId, Guid PublishedId, Guid DraftId)> SeedAsync()
    {
        var template = EmailTemplate
            .Create(EmailScope.Tenant, _tenantId, "welcome", "Hi", null, null, "[]", null)
            .Value;
        var first = NewVersion(template.Id, 1);
        var second = NewVersion(template.Id, 2);

        await using var seed = _database.NewContext();
        seed.EmailTemplates.Add(template);
        seed.EmailTemplateVersions.AddRange(first, second);
        await seed.SaveChangesAsync();
        return (template.Id, first.Id, second.Id);
    }

    private async Task ArchiveAsync(Guid templateId)
    {
        await using var db = _database.NewContext();
        var template = await db.EmailTemplates.FirstAsync(t => t.Id == templateId);
        Assert.True(template.Archive().IsSuccess);
        await db.SaveChangesAsync();
    }

    private static EmailTemplateVersion NewVersion(Guid templateId, int number) =>
        EmailTemplateVersion
            .Create(templateId, number, "Hi", $"key-{number}", Guid.NewGuid(), null, null, null, null, null)
            .Value;
}
