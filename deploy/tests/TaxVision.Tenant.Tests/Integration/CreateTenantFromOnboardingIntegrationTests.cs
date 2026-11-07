using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Tenant.Application.Tenants.Commands;
using Wolverine;
using Wolverine.Tracking;
using Wolverine.Transports.Tcp;
using DomainTenant = TaxVision.Tenant.Domain.Tenant;

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>
/// Dos altas del mismo onboarding contra SQL real y el pipeline de Wolverine: la perdedora choca con el índice único,
/// el reintento en línea encuentra a la ganadora y el aviso del intento revertido no sale. El puerto escucha para que
/// lo enviado se vea: el sobre del intento revertido no queda en la base, sale desde la memoria del contexto.
/// </summary>
public sealed class CreateTenantFromOnboardingIntegrationTests : IAsyncLifetime
{
    private readonly TenantTemporaryDatabase _database = new();
    private readonly RivalTenantInterceptor _rival = new();
    private IHost? _host;

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        try
        {
            _host = await TenantWolverineHost.StartAsync(
                _database.ConnectionString,
                (options, port) =>
                {
                    options.Discovery.IncludeType(typeof(CreateTenantFromOnboardingHandler));
                    options.ListenAtPort(port);
                    options
                        .PublishMessage<TenantCreatedForOnboardingIntegrationEvent>()
                        .ToPort(port)
                        .UseDurableOutbox();
                    options.PublishMessage<TenantCreatedIntegrationEvent>().ToPort(port).UseDurableOutbox();
                    options.UseTaxVisionConflictPolicy();
                    options.ApplyStandardFailurePolicies();
                },
                _rival
            );
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        await TenantWolverineHost.StopAsync(_host);
        await _database.DropAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task LosingTheUniqueIndexRace_SucceedsWithoutPublishingTheRevertedId()
    {
        var command = NewCommand();
        var winner = DomainTenant.Create("Winner", "winner-tax", "Etc/UTC", command.OnboardingId).Value;
        _rival.BeforeInsert = async () =>
        {
            await using var db = _database.NewContext();
            db.Tenants.Add(winner);
            await db.SaveChangesAsync();
        };

        var (result, session) = await InvokeAsync(command);

        Assert.True(result!.IsSuccess);
        await using var read = _database.NewContext();
        var stored = await read.Tenants.AsNoTracking().SingleAsync(t => t.OnboardingId == command.OnboardingId);
        Assert.Equal(winner.Id, stored.Id);
        Assert.Equal(
            [],
            session.Sent.MessagesOf<TenantCreatedForOnboardingIntegrationEvent>().Select(evt => evt.CreatedTenantId)
        );
        Assert.Equal([], session.Sent.MessagesOf<TenantCreatedIntegrationEvent>().Select(evt => evt.NewTenantId));
    }

    [Fact(Timeout = 120_000)]
    public async Task WithoutRace_SendsBothNoticesWithTheStoredId()
    {
        var command = NewCommand();

        var (result, session) = await InvokeAsync(command);

        Assert.True(result!.IsSuccess);
        await using var read = _database.NewContext();
        var stored = await read.Tenants.AsNoTracking().SingleAsync(t => t.OnboardingId == command.OnboardingId);
        Assert.Equal(
            stored.Id,
            Assert.Single(session.Sent.MessagesOf<TenantCreatedForOnboardingIntegrationEvent>()).CreatedTenantId
        );
        Assert.Equal(stored.Id, Assert.Single(session.Sent.MessagesOf<TenantCreatedIntegrationEvent>()).NewTenantId);
    }

    private async Task<(Result? Result, ITrackedSession Session)> InvokeAsync(CreateTenantFromOnboardingCommand command)
    {
        Result? result = null;
        var session = await _host!
            .TrackActivity()
            .IncludeExternalTransports()
            .DoNotAssertOnExceptionsDetected()
            .Timeout(TimeSpan.FromSeconds(60))
            .ExecuteAndWaitAsync(
                (Func<IMessageContext, Task>)(async context => result = await context.InvokeAsync<Result>(command))
            );
        return (result, session);
    }

    private static CreateTenantFromOnboardingCommand NewCommand() =>
        new(Guid.NewGuid(), "Acme Tax Office", "acme-tax", "owner@acme.com", DateTime.UtcNow);

    /// <summary>Deja guardar a la ganadora justo antes del INSERT de la perdedora: un choque real con el índice.</summary>
    private sealed class RivalTenantInterceptor : SaveChangesInterceptor
    {
        public Func<Task>? BeforeInsert { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            var inserting = eventData
                .Context!.ChangeTracker.Entries<DomainTenant>()
                .Any(entry => entry.State == EntityState.Added);
            if (BeforeInsert is { } rival && inserting)
            {
                BeforeInsert = null;
                await rival();
            }
            return result;
        }
    }
}
