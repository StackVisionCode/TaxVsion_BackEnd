using System.Net;
using System.Text;
using BuildingBlocks.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.FinancialInsights.Domain.Projections;
using TaxVision.FinancialInsights.Infrastructure.Tenants;
using TaxVision.FinancialInsights.Tests.TestDoubles;

namespace TaxVision.FinancialInsights.Tests.Infrastructure;

public sealed class TenantDirectoryReconciliationTests
{
    [Fact]
    public async Task Reconciler_inserts_missing_offices_with_their_status_across_pages()
    {
        var alpha = Guid.NewGuid();
        var beta = Guid.NewGuid();
        var gamma = Guid.NewGuid();
        await using var h = new FinancialTestHarness(alpha);
        var client = new FakeTenantDirectory(
            new RemoteTenant(alpha, "Alpha", "Active", true, 3),
            new RemoteTenant(beta, "Beta", "Suspended", false, 5),
            new RemoteTenant(gamma, "Gamma", "Active", true, 1)
        );

        var outcome = await Reconciler(h, client).RunAsync(pageSize: 2, CancellationToken.None);

        Assert.True(outcome.TenantReachable);
        Assert.Equal(3, outcome.Inserted);
        Assert.Equal(2, client.Calls);
        var rows = await h.Db.TenantDirectoryProjections.IgnoreQueryFilters().ToDictionaryAsync(r => r.Id);
        Assert.Equal("Beta", rows[beta].Name);
        Assert.False(rows[beta].IsActive);
        Assert.Equal("Suspended", rows[beta].Status);
        Assert.Equal(5, rows[beta].TenantVersion);
        Assert.True(rows[gamma].IsActive);
    }

    [Fact]
    public async Task Reconciler_refreshes_stale_rows_and_keeps_newer_ones()
    {
        var stale = Guid.NewGuid();
        var newer = Guid.NewGuid();
        await using var h = new FinancialTestHarness(stale);
        var staleRow = TenantDirectoryProjection.Create(stale, string.Empty, DateTime.UtcNow.AddDays(-1));
        staleRow.ApplyStatus("Active", true, 1, DateTime.UtcNow.AddDays(-1));
        var newerRow = TenantDirectoryProjection.Create(newer, "Renamed", DateTime.UtcNow.AddMinutes(-1));
        newerRow.ApplyStatus("Suspended", false, 9, DateTime.UtcNow.AddMinutes(-1));
        h.Db.TenantDirectoryProjections.AddRange(staleRow, newerRow);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var client = new FakeTenantDirectory(
            new RemoteTenant(stale, "Stale Office", "Suspended", false, 2),
            new RemoteTenant(newer, "Old Name", "Active", true, 8)
        );

        var outcome = await Reconciler(h, client).RunAsync(pageSize: 200, CancellationToken.None);

        Assert.Equal(0, outcome.Inserted);
        var rows = await h
            .Db.TenantDirectoryProjections.IgnoreQueryFilters()
            .AsNoTracking()
            .ToDictionaryAsync(r => r.Id);
        Assert.Equal("Stale Office", rows[stale].Name);
        Assert.False(rows[stale].IsActive);
        Assert.Equal("Renamed", rows[newer].Name);
        Assert.False(rows[newer].IsActive);
        Assert.Equal(9, rows[newer].TenantVersion);
    }

    [Fact]
    public async Task Reconciler_without_status_from_tenant_keeps_the_known_status()
    {
        var office = Guid.NewGuid();
        await using var h = new FinancialTestHarness(office);
        var client = new FakeTenantDirectory(new RemoteTenant(office, "Office", null, null, null));

        await Reconciler(h, client).RunAsync(pageSize: 200, CancellationToken.None);

        var row = await h.Db.TenantDirectoryProjections.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("Office", row.Name);
        Assert.True(row.IsActive);
    }

    [Fact]
    public async Task Reconciler_aborts_when_tenant_does_not_answer()
    {
        await using var h = new FinancialTestHarness(Guid.NewGuid());

        var outcome = await Reconciler(h, new FakeTenantDirectory(unreachable: true))
            .RunAsync(200, CancellationToken.None);

        Assert.False(outcome.TenantReachable);
        Assert.Empty(await h.Db.TenantDirectoryProjections.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Client_reads_status_and_version_with_a_platform_token()
    {
        var id = Guid.NewGuid();
        string? path = null;
        string? token = null;
        var client = new FinancialTenantDirectoryClient(
            new HttpClient(
                new Handler(request =>
                {
                    path = request.RequestUri!.PathAndQuery;
                    token = request.Headers.Authorization?.Parameter;
                    return Json(
                        $$"""[{"id":"{{id}}","name":"Alpha","subdomain":"alpha","defaultTimeZoneId":"UTC","status":"Suspended","isActive":false,"version":4}]"""
                    );
                })
            )
            {
                BaseAddress = new Uri("https://tenant.test/"),
            },
            new StubTokens("platform-token"),
            NullLogger<FinancialTenantDirectoryClient>.Instance
        );

        var page = await client.ListPageAsync(2, 50, CancellationToken.None);

        Assert.Equal("/internal/tenants/directory?page=2&size=50", path);
        Assert.Equal("platform-token", token);
        Assert.Equal(new RemoteTenant(id, "Alpha", "Suspended", false, 4), Assert.Single(page!));
    }

    [Fact]
    public async Task Client_returns_null_when_tenant_fails_or_there_is_no_token()
    {
        var failing = new FinancialTenantDirectoryClient(
            new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)))
            {
                BaseAddress = new Uri("https://tenant.test/"),
            },
            new StubTokens("t"),
            NullLogger<FinancialTenantDirectoryClient>.Instance
        );
        var anonymous = new FinancialTenantDirectoryClient(
            new HttpClient(new Handler(_ => Json("[]"))) { BaseAddress = new Uri("https://tenant.test/") },
            new StubTokens(null),
            NullLogger<FinancialTenantDirectoryClient>.Instance
        );

        Assert.Null(await failing.ListPageAsync(1, 10, CancellationToken.None));
        Assert.Null(await anonymous.ListPageAsync(1, 10, CancellationToken.None));
    }

    private static TenantDirectoryReconciler Reconciler(
        FinancialTestHarness h,
        IFinancialTenantDirectoryClient client
    ) => new(client, h.Db, NullLogger<TenantDirectoryReconciler>.Instance);

    private sealed class FakeTenantDirectory(params RemoteTenant[] tenants) : IFinancialTenantDirectoryClient
    {
        private readonly bool _unreachable;

        public FakeTenantDirectory(bool unreachable)
            : this()
        {
            _unreachable = unreachable;
        }

        public int Calls { get; private set; }

        public Task<IReadOnlyList<RemoteTenant>?> ListPageAsync(int page, int size, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<RemoteTenant>?>(
                _unreachable ? null : tenants.Skip((page - 1) * size).Take(size).ToArray()
            );
        }
    }

    private sealed class StubTokens(string? token) : IServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(token);
    }

    private static HttpResponseMessage Json(string content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(respond(request));
    }
}
