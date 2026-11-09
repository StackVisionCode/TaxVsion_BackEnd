using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using JasperFx.CodeGeneration.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;

namespace TaxVision.CloudStorage.Tests.Integration;

/// <summary>
/// Lo que ve quien reserva por HTTP (InvokeAsync) con las políticas de Program.cs: Storage.QuotaExceeded no es un
/// conflicto de versión, así que el reintento en línea relee la fila y el dominio responde el error de siempre.
/// </summary>
[Collection(CloudStorageConcurrencyCollection.Name)]
public sealed class QuotaConflictInvokeIntegrationTests(CloudStorageConcurrencyFixture fixture)
{
    private const string Schema = "cst_quota_invoke_it";
    private const long Max = 1_000;
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task A_reservation_that_loses_the_race_ends_as_the_usual_quota_error()
    {
        await using (var db = fixture.NewContext(_tenantId))
        {
            db.StorageLimits.Add(TenantStorageLimit.Create(_tenantId, "starter", Max, Max));
            await db.SaveChangesAsync();
        }

        using var host = await StartHostAsync();
        var command = new ReserveProbeCommand(_tenantId, 600, () => ReserveInAnotherContextAsync(600));

        var result = await host.MessageBus().InvokeAsync<Result>(command);

        Assert.Equal(QuotaErrors.Exceeded, result.Error);
        Assert.Equal(2, command.Attempts);
        await using var check = fixture.NewContext(_tenantId);
        Assert.Equal(600, (await check.StorageLimits.AsNoTracking().SingleAsync()).ReservedBytes);
        await host.StopAsync(TimeSpan.FromSeconds(10));
    }

    private async Task ReserveInAnotherContextAsync(long bytes)
    {
        await using var rival = fixture.NewContext(_tenantId);
        var limit = await rival.StorageLimits.SingleAsync();
        Assert.True(limit.Reserve(bytes).IsSuccess);
        await rival.SaveChangesAsync();
    }

    private async Task<IHost> StartHostAsync()
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<CloudStorageDbContext>(o => o.UseSqlServer(fixture.ConnectionString));
                services.AddSingleton<ITenantContext>(new TemporaryDatabase.FixedTenant(_tenantId));
                services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CloudStorageDbContext>());
                services.AddScoped<IStorageLimitRepository, StorageLimitRepository>();
            })
            .UseWolverine(options =>
            {
                options.Discovery.DisableConventionalDiscovery().IncludeType(typeof(ReserveProbeHandler));
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Durability.Mode = DurabilityMode.Solo;
                options.PersistMessagesWithSqlServer(fixture.ConnectionString, Schema);
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, CloudStorageDbContext>();
                options.Policies.AutoApplyTransactions();
                options.UseTaxVisionConflictPolicy();
                options.ApplyStandardFailurePolicies();
            })
            .Build();
        await host.StartAsync();
        return host;
    }
}

public sealed record ReserveProbeCommand(Guid TenantId, long Bytes, Func<Task> OnFirstRead)
{
    public int Attempts { get; set; }
}

public static class ReserveProbeHandler
{
    public static async Task<Result> Handle(
        ReserveProbeCommand command,
        IStorageLimitRepository limits,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var limit = await limits.GetAsync(command.TenantId, ct);
        if (++command.Attempts == 1)
            await command.OnFirstRead();

        var reserved = limit!.Reserve(command.Bytes);
        if (reserved.IsFailure)
            return reserved;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
