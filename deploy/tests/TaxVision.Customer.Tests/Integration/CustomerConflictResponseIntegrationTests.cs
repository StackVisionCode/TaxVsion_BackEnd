using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Infrastructure.Persistence;
using Xunit;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Integration;

/// <summary>
/// CUS-1 y CUS-2 por HTTP con el host real sobre la base temporal: un conflicto llega al CRM como 409 con su
/// código, no como 500. BB-11: el de versión sale de inmediato; <c>Customer.CreationBusy</c> sigue la regla general
/// y tarda ~21 s (1, 5 y 15 s).
/// </summary>
public sealed class CustomerConflictResponseIntegrationTests(
    CustomerApiFactory baseFactory,
    CustomerTemporaryDatabase database
) : IClassFixture<CustomerApiFactory>, IClassFixture<CustomerTemporaryDatabase>
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();

    [Fact]
    public async Task UpdateCustomer_StaleVersion_Returns409()
    {
        var id = await SeedAsync();
        var client = Client(services =>
            services.ConfigureDbContext<CustomerDbContext>(db => db.AddInterceptors(new StaleVersionOnSave()))
        );

        var clock = Stopwatch.StartNew();
        var response = await client.PatchAsJsonAsync(
            $"/customers/{id}",
            new { Language = "En", PrimaryEmail = $"cus1-{Guid.NewGuid():N}@example.test" }
        );
        clock.Stop();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Persistence.ConcurrencyConflict", await CodeOfAsync(response));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), $"Tardó {clock.Elapsed.TotalSeconds:F1} s.");
    }

    [Fact]
    public async Task CreateCustomer_CreationLockBusy_Returns409()
    {
        var client = Client(services =>
            services.Replace(ServiceDescriptor.Scoped<ICustomerCreationLock>(_ => new BusyLock()))
        );

        var response = await client.PostAsJsonAsync(
            "/customers",
            new
            {
                Kind = "Individual",
                FirstName = "Busy",
                LastName = "Client",
                PrimaryEmail = $"cus2-{Guid.NewGuid():N}@example.test",
                Language = "En",
            }
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Customer.CreationBusy", await CodeOfAsync(response));
    }

    private HttpClient Client(Action<IServiceCollection> configure)
    {
        var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.ConfigureDbContext<CustomerDbContext>(db => db.UseSqlServer(database.ConnectionString));
                configure(services);
            })
        );
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokenFactory.Mint(baseFactory, _tenant, _user)
        );
        return client;
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString();
    }

    private async Task<Guid> SeedAsync()
    {
        var customer = DomainCustomer
            .Register(
                _tenant,
                CustomerKind.Individual,
                PersonalName.Create("Stale", "Client").Value,
                null,
                EmailAddress.Create($"cus1-{Guid.NewGuid():N}@example.test").Value,
                null,
                Language.En,
                _user
            )
            .Value;
        await using var db = database.NewContext(_tenant);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    // Lo que EF lanza cuando el UPDATE de un cliente no afecta filas porque la RowVersion ya cambió.
    private sealed class StaleVersionOnSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) =>
            eventData.Context!.ChangeTracker.Entries<DomainCustomer>().Any(e => e.State == EntityState.Modified)
                ? throw new DbUpdateConcurrencyException("The customer row version changed.")
                : ValueTask.FromResult(result);
    }

    private sealed class BusyLock : ICustomerCreationLock
    {
        public Task AcquireAsync(Guid tenantId, string normalizedEmail, CancellationToken ct) =>
            throw new TimeoutException("The customer creation lock is busy.");
    }
}
