using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Persistence.Repositories;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// Contra SQL Server: los intentos en paralelo sobre una misma cuenta se ponen en fila. Sin esto, RowVersion
/// respondía 409 solo para los correos que existen y cada perdedor volvía a calcular el hash en el reintento.
/// </summary>
public sealed class LoginConcurrencyIntegrationTests : IAsyncLifetime
{
    private const string Email = "ana@example.test";
    private static readonly TimeSpan BarrierTimeout = TimeSpan.FromSeconds(10);

    private readonly AccountSessionFixture _world = new();
    private AuthTestDatabase _database = null!;
    private Guid _userId;

    public async Task InitializeAsync()
    {
        _database = await AuthTestDatabase.CreateAsync();
        var user = User.Register(
            _world.Tenant.Id,
            "Ana",
            "Test",
            Email,
            "hashed:right",
            UserActorType.TenantEmployee
        ).Value;
        _userId = user.Id;
        await using var db = _database.NewContext(_world.Tenant.Id);
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task WrongPasswordsInParallel_AllAreCountedWithoutConflict()
    {
        // Más intentos que el tope: los que llegan con la cuenta ya bloqueada no repiten la alerta.
        const int attempts = LockoutPolicy.MaxFailedAttempts + 2;
        using var barrier = new Barrier(attempts);
        var buses = Enumerable.Range(0, attempts).Select(_ => new FakeMessageBus()).ToList();

        var results = await Task.WhenAll(buses.Select(bus => Task.Run(() => LoginAsync("wrong", barrier, bus))));

        Assert.All(results, result => Assert.Equal("Auth.Invalid", result.Error.Code));
        Assert.Single(buses.SelectMany(bus => bus.Published).OfType<SecurityAlertIntegrationEvent>());
        await using var read = _database.NewContext(_world.Tenant.Id);
        var user = await read.Users.AsNoTracking().SingleAsync(u => u.Id == _userId);
        Assert.True(user.IsLockedOut(DateTime.UtcNow));
    }

    [Fact]
    public async Task RightPasswordsInParallel_AfterAFailure_BothClearTheCounterWithoutConflict()
    {
        await using (var seed = _database.NewContext(_world.Tenant.Id))
        {
            var user = await seed.Users.SingleAsync(u => u.Id == _userId);
            user.RegisterFailedLogin(DateTime.UtcNow, LockoutPolicy.MaxFailedAttempts, LockoutPolicy.LockoutDuration);
            await seed.SaveChangesAsync();
        }
        using var barrier = new Barrier(2);

        var results = await Task.WhenAll(
            Task.Run(() => LoginAsync("right", barrier, new FakeMessageBus())),
            Task.Run(() => LoginAsync("right", barrier, new FakeMessageBus()))
        );

        Assert.All(results, result => Assert.True(result.IsSuccess));
        await using var read = _database.NewContext(_world.Tenant.Id);
        Assert.Equal(0, (await read.Users.AsNoTracking().SingleAsync(u => u.Id == _userId)).FailedLoginCount);
    }

    private async Task<Result<LoginResponse>> LoginAsync(string password, Barrier barrier, FakeMessageBus bus)
    {
        await using var db = _database.NewContext(_world.Tenant.Id);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var steps = LoginStepsFixture.Build(
            _world,
            new SqlUserLoginLock(db),
            new NoMfaRepository(),
            new NoopSessionTakeoverTicketStore(),
            new PermissiveLoginThrottler(),
            new MfaOptions { Enforced = false },
            users: new UserRepository(db),
            hasher: new BarrierHasher(barrier),
            audit: new FakeAuthAuditWriter(),
            request: new FakeRequestContext(),
            correlation: new FakeCorrelationContext()
        );
        var result = await LoginHandler.Handle(
            new LoginCommand(_world.Tenant.Id, Email, password, AccountKind: UserAccountKind.Staff),
            steps.Discovery,
            steps.Password,
            steps.Mfa,
            steps.Session,
            db,
            bus,
            CancellationToken.None
        );
        await transaction.CommitAsync();
        return result;
    }

    /// <summary>Espera a los demás justo después de leer la cuenta, para forzar la carrera.</summary>
    private sealed class BarrierHasher(Barrier barrier) : IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";

        public bool Verify(string password, string hash)
        {
            barrier.SignalAndWait(BarrierTimeout);
            return hash == Hash(password);
        }
    }
}
