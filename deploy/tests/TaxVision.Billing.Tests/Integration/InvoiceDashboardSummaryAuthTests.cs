using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Billing.Domain.Permissions;
using TaxVision.Billing.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Billing.Tests.Integration;

/// <summary>
/// El resumen del dashboard contra Billing.Api real: exige <c>invoicing.summary.view</c> (proyección local de permisos).
/// Ver facturas (<c>invoicing.view</c>) no basta.
/// </summary>
[Collection(BillingTestDatabase.Collection)]
public sealed class InvoiceDashboardSummaryAuthTests(BillingApiFactory factory, BillingTestDatabase database)
    : IClassFixture<BillingApiFactory>
{
    private const string Route = "/billing/invoices/dashboard-summary";

    [Fact]
    public async Task An_employee_with_only_invoicing_view_is_forbidden()
    {
        var client = await EmployeeAsync(["invoicing.view"]);

        var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_employee_with_the_summary_permission_gets_the_summary()
    {
        var client = await EmployeeAsync(["invoicing.summary.view"]);

        var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("currency").ValueKind);
        Assert.Equal(6, body.GetProperty("revenue").GetProperty("monthly").GetArrayLength());
        Assert.Equal(4, body.GetProperty("aging").GetProperty("buckets").GetArrayLength());
    }

    // Empleado con su fila de permisos en la proyección local (versión 0 = la del token sin perm_v).
    private async Task<HttpClient> EmployeeAsync(IReadOnlyCollection<string> permissions)
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using (
            var db = new BillingDbContext(
                new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer(database.ConnectionString).Options,
                new NoTenant()
            )
        )
        {
            db.AuthzUserPermissionsProjections.Add(
                AuthzUserPermissionsProjection.Create(tenantId, userId, 0, permissions, [])
            );
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        var token = JwtTestTokenFactory.MintActor(factory, tenantId, userId, "TenantEmployee");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
