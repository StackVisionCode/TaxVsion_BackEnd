using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Infrastructure.Persistence;

namespace TaxVision.PaymentApp.Tests.Integration;

/// <summary>PAY-6 contra el SQL Server local: dos cambios de método por defecto a la vez no dejan dos defaults.</summary>
public sealed class TenantProviderCustomerConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private Guid _customerId;
    private Guid _methodA;
    private Guid _methodB;

    public async Task InitializeAsync()
    {
        var nowUtc = DateTime.UtcNow;
        var customer = TenantProviderCustomer
            .Register(
                _tenantId,
                PaymentProviderCode.Stripe,
                ProviderCustomerReference.Create(PaymentProviderCode.Stripe, $"cus_{Guid.NewGuid():N}").Value,
                "office@test.local",
                nowUtc
            )
            .Value;
        _methodA = customer.AttachPaymentMethod("pm_a", "visa", "4242", 12, 2030, true, nowUtc).Value;
        _methodB = customer.AttachPaymentMethod("pm_b", "visa", "1111", 12, 2030, false, nowUtc).Value;
        _customerId = customer.Id;

        await using var db = NewContext();
        db.TenantProviderCustomers.Add(customer);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Set<TenantSavedPaymentMethod>()
            .IgnoreQueryFilters()
            .Where(m => m.TenantId == _tenantId)
            .ExecuteDeleteAsync();
        await db.TenantProviderCustomers.IgnoreQueryFilters().Where(c => c.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Set_default_and_attach_default_in_parallel_leave_one_default()
    {
        await using var first = NewContext();
        await using var second = NewContext();
        var firstCustomer = await LoadAsync(first);
        var secondCustomer = await LoadAsync(second);

        Assert.True(firstCustomer.MarkPaymentMethodAsDefault(_methodB, DateTime.UtcNow).IsSuccess);
        Assert.True(
            secondCustomer.AttachPaymentMethod("pm_c", "visa", "0005", 12, 2030, true, DateTime.UtcNow).IsSuccess
        );

        await first.SaveChangesAsync();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());

        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
        Assert.Equal(1, await CountDefaultsAsync());
    }

    [Fact]
    public async Task Unique_default_index_rejects_a_second_default_written_bypassing_the_aggregate()
    {
        await using var connection = new SqlConnection(PaymentAppWolverineHost.Connection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE dbo.TenantSavedPaymentMethods SET IsDefault = 1 WHERE Id = @id";
        command.Parameters.AddWithValue("@id", _methodB);

        var error = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());

        Assert.Equal(2601, error.Number);
    }

    // El índice filtrado no ordena los UPDATE de EF: cambiar de default en ambos sentidos tiene que seguir guardando.
    [Fact]
    public async Task Default_can_move_between_methods_in_both_directions()
    {
        foreach (var method in new[] { _methodB, _methodA, _methodB })
        {
            await using var db = NewContext();
            var customer = await LoadAsync(db);
            Assert.True(customer.MarkPaymentMethodAsDefault(method, DateTime.UtcNow).IsSuccess);
            await db.SaveChangesAsync();
            Assert.Equal(1, await CountDefaultsAsync());
        }
    }

    [Fact]
    public async Task Attaching_or_replacing_a_default_keeps_a_single_default()
    {
        await using (var attach = NewContext())
        {
            var customer = await LoadAsync(attach);
            customer.AttachPaymentMethod("pm_c", "visa", "0005", 12, 2030, true, DateTime.UtcNow);
            await attach.SaveChangesAsync();
        }
        Assert.Equal(1, await CountDefaultsAsync());

        await using (var replace = NewContext())
        {
            var customer = await LoadAsync(replace);
            customer.ReplaceDefaultPaymentMethod("pm_d", "visa", "0006", 12, 2030, DateTime.UtcNow);
            await replace.SaveChangesAsync();
        }
        Assert.Equal(1, await CountDefaultsAsync());
    }

    [Fact]
    public async Task Detaching_the_default_promotes_another_method()
    {
        foreach (var method in new[] { _methodA, _methodB })
        {
            await using var seed = NewContext();
            var customer = await LoadAsync(seed);
            customer.AttachPaymentMethod($"pm_new_{method:N}", "visa", "0007", 12, 2030, false, DateTime.UtcNow);
            customer.DetachPaymentMethod(customer.GetDefaultMethod()!.Id, DateTime.UtcNow);
            await seed.SaveChangesAsync();
            Assert.Equal(1, await CountDefaultsAsync());
        }
    }

    private Task<TenantProviderCustomer> LoadAsync(PaymentAppDbContext db) =>
        db.TenantProviderCustomers.Include(c => c.SavedMethods).SingleAsync(c => c.Id == _customerId);

    private async Task<int> CountDefaultsAsync()
    {
        await using var db = NewContext();
        return await db.Set<TenantSavedPaymentMethod>()
            .IgnoreQueryFilters()
            .CountAsync(m => m.TenantProviderCustomerId == _customerId && m.IsDefault && !m.IsDetached);
    }

    private PaymentAppDbContext NewContext()
    {
        var tenant = new ReconcileStuckSaaSPaymentIntegrationTests.TestTenantContext();
        tenant.SetTenant(_tenantId);
        return new PaymentAppDbContext(
            new DbContextOptionsBuilder<PaymentAppDbContext>().UseSqlServer(PaymentAppWolverineHost.Connection).Options,
            tenant
        );
    }
}
