using BuildingBlocks.Tenancy;
using BuildingBlocks.TimeZones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests.Queries.List;
using TaxVision.Signature.Application.Requests.Queries.Summary;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Infrastructure.Persistence;
using TaxVision.Signature.Infrastructure.Persistence.Queries;
using TaxVision.Signature.Tests.Application.Offboarding;
using static TaxVision.Signature.Tests.Integration.Performance.DashboardPerformance;

namespace TaxVision.Signature.Tests.Integration.Performance;

/// <summary>
/// Volumen del dashboard en Signature: 20 000 solicitudes (con su documento y su firmante mapeado) en un tenant de
/// prueba y tres de ruido con un tercio, en la base temporal BB-10. Mide el resumen y el listado expiring /
/// assignee=me con la oficina completa y con un empleado de 500 clientes asignados de 20 000.
/// </summary>
[Trait("Category", "Performance")]
public sealed class SignatureDashboardPerformanceTests : IAsyncLifetime
{
    private const int Requests = 20_000;
    private const int Customers = 20_000;
    private const int Employees = 40;
    private static readonly DateTime Now = new(2026, 11, 21, 15, 0, 0, DateTimeKind.Utc);
    private static readonly ResolvedTimeZone Zone = ResolvedTimeZone.Resolve("America/New_York");
    private static readonly Guid[] Tenants = [.. AllTenants];
    private static readonly Guid Employee = Id("a5e5", 1, 0);
    private static readonly string ConnectionString = SignatureIntegrationDatabase.ConnectionString;
    private static readonly string[] Children =
    [
        "[dbo].[Signers]",
        "[dbo].[RequestDocuments]",
        "[dbo].[SignatureFields]",
    ];
    private static readonly string[] OwnTables = ["[dbo].[SignatureRequests]", "[dbo].[CustomerAssignmentProjections]"];

    public async Task InitializeAsync()
    {
        var template = await TemplateAsync();
        for (var t = 1; t <= Tenants.Length; t++)
            await SeedTenantAsync(t, template, DivisorOf(t));
        await UpdateStatisticsAsync(ConnectionString, [.. OwnTables, .. Children]);
    }

    public async Task DisposeAsync()
    {
        foreach (var child in Children)
            await DeleteChildrenAsync(ConnectionString, child, "SignatureRequestId", OwnTables[0], Tenants);
        await DeleteAsync(ConnectionString, Tenants, OwnTables);
        Assert.All((await CountAsync(ConnectionString, Tenants, OwnTables)).Values, count => Assert.Equal(0, count));
        Assert.All(
            (await CountAsync(FleetConnectionString(), Tenants, OwnTables)).Values,
            count => Assert.Equal(0, count)
        );
    }

    [Fact]
    public async Task Summary_and_dashboard_lists_with_volume()
    {
        Assert.Equal(Requests + 1, (await CountAsync(ConnectionString, [Tenants[0]], OwnTables[0]))[OwnTables[0]]);

        foreach (var (scope, canViewAll) in new[] { ("oficina", true), ("empleado 500/20 000", false) })
        {
            await MeasureAsync(
                "Signature",
                "GET /signature/requests/summary",
                scope,
                ConnectionString,
                async capture =>
                {
                    await using var db = NewContext(capture);
                    var result = await GetSignatureRequestsSummaryHandler.Handle(
                        new GetSignatureRequestsSummaryQuery(Tenants[0], Employee, canViewAll, null, null, Now),
                        new SignatureRequestsSummaryReader(db),
                        new SignatureAnalyticsReadService(db),
                        new FixedZone(),
                        Visibility,
                        default
                    );
                    Assert.True(result.IsSuccess);
                }
            );

            foreach (
                var (piece, expiring, mine) in new[] { ("expiring=true", true, false), ("assignee=me", false, true) }
            )
                await MeasureAsync(
                    "Signature",
                    $"GET /signature/requests?{piece}",
                    scope,
                    ConnectionString,
                    async capture =>
                    {
                        await using var db = NewContext(capture);
                        var result = await new SignatureRequestReadService(db, Visibility).ListAsync(
                            new ListSignatureRequestsQuery(
                                Tenants[0],
                                null,
                                null,
                                1,
                                20,
                                Employee,
                                canViewAll,
                                ExpiringSoon: expiring,
                                MineOnly: mine,
                                NowUtc: Now
                            )
                        );
                        Assert.True(result.TotalCount > 0);
                    }
                );
        }
    }

    private static readonly IOptions<SignatureVisibilityOptions> Visibility = Options.Create(
        new SignatureVisibilityOptions { Enabled = true }
    );

    // Una solicitud real enviada (documento, firmante y campo): las copias heredan columnas válidas.
    private static async Task<Guid> TemplateAsync()
    {
        var request = PreparerRequests.Draft(Tenants[0], Employee);
        var document = request.Documents.Single();
        var signer = request
            .AddSigner(
                TestSignatureLimits.Default,
                SignerEmail.Create("client@example.test").Value,
                SignerFullName.Create("Client Signer").Value,
                Id("c057", 1, 0)
            )
            .Value;
        Assert.True(
            request
                .PlaceField(
                    TestSignatureLimits.MaxFieldsPerSigner,
                    signer.Id,
                    document.Id,
                    SignatureFieldKind.Signature,
                    FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value,
                    null,
                    true
                )
                .IsSuccess
        );
        request.AttachVerifiedDocument(document.Id, VerifiedDocument.Create(PreparerRequests.Hash('a'), 50).Value);
        Assert.True(request.Send(Now.AddDays(-1)).IsSuccess);
        await using var db = NewContext(null, Tenants[0]);
        db.SignatureRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private static async Task SeedTenantAsync(int t, Guid template, int divisor)
    {
        var tenant = $"'{Tenants[t - 1]}'";
        var requestId = SqlId("5160", t, "n");
        var created =
            "DATEADD(MINUTE, -(n % 1440), DATEADD(DAY, -(n % 365), CAST('2026-11-21T14:00:00' AS datetime2)))";
        var sent = $"DATEADD(HOUR, 1, {created})";
        // Por n % 10: 1 borrador, 3 en curso, 4 firmadas, 1 vencida y 1 cancelada.
        var status =
            "CASE WHEN n % 10 = 0 THEN N'Draft' WHEN n % 10 < 4 THEN N'InProgress' WHEN n % 10 < 8 THEN N'Completed' "
            + "WHEN n % 10 = 8 THEN N'Expired' ELSE N'Canceled' END";

        await CloneAsync(
            ConnectionString,
            "[dbo].[SignatureRequests]",
            $"[Id] = '{template}'",
            Requests / divisor,
            new Dictionary<string, string>
            {
                ["Id"] = requestId,
                ["TenantId"] = tenant,
                ["Status"] = status,
                ["CreatedByUserId"] = SqlId("a5e5", t, $"(n / 7) % {Employees}"),
                ["CreatedAtUtc"] = created,
                ["SentAtUtc"] = $"CASE WHEN n % 10 = 0 THEN NULL ELSE {sent} END",
                ["CompletedAtUtc"] = $"CASE WHEN n % 10 BETWEEN 4 AND 7 THEN DATEADD(MINUTE, 5 + n % 600, {sent}) END",
                ["ExpiredAtUtc"] = $"CASE WHEN n % 10 = 8 THEN DATEADD(DAY, 30, {sent}) END",
                ["CanceledAtUtc"] = $"CASE WHEN n % 10 = 9 THEN DATEADD(DAY, 2, {sent}) END",
                // 1 de cada 7 en curso vence en las próximas 40 h.
                ["ExpiresAtUtc"] =
                    $"CASE WHEN n % 7 = 0 THEN DATEADD(HOUR, 1 + n % 40, CAST('2026-11-21T15:00:00' AS datetime2)) ELSE DATEADD(DAY, 30, {sent}) END",
            }
        );
        await CloneAsync(
            ConnectionString,
            "[dbo].[RequestDocuments]",
            $"[SignatureRequestId] = '{template}'",
            Requests / divisor,
            new Dictionary<string, string> { ["Id"] = "NEWID()", ["SignatureRequestId"] = requestId }
        );
        await CloneAsync(
            ConnectionString,
            "[dbo].[Signers]",
            $"[SignatureRequestId] = '{template}'",
            Requests / divisor,
            new Dictionary<string, string>
            {
                ["Id"] = "NEWID()",
                ["SignatureRequestId"] = requestId,
                ["MappedCustomerId"] = SqlId("c057", t, $"n % {Customers}"),
            }
        );
        await InsertAsync(
            ConnectionString,
            "[dbo].[CustomerAssignmentProjections]",
            Customers / divisor,
            new Dictionary<string, string>
            {
                ["Id"] = "NEWID()",
                ["TenantId"] = tenant,
                ["CustomerId"] = SqlId("c057", t, "n"),
                ["UserId"] = SqlId("a5e5", t, $"(n / 50) % {Employees}"),
                ["Version"] = "SYSUTCDATETIME()",
            }
        );
    }

    private static string FleetConnectionString() =>
        Environment.GetEnvironmentVariable("SIGNATURE_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("4a1e9c8d-2f7b-4e15-9c3d-signature10001")
            .Build()
            .GetConnectionString("Default")!;

    private static SignatureDbContext NewContext(CommandCapture? capture, Guid tenant = default)
    {
        var options = new DbContextOptionsBuilder<SignatureDbContext>().UseSqlServer(ConnectionString);
        if (capture is not null)
            options.AddInterceptors(capture);
        return new SignatureDbContext(options.Options, new FixedTenant(tenant));
    }

    private sealed class FixedZone : IOfficeTimeZone
    {
        public Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct) => Task.FromResult(Zone);
    }

    private sealed class FixedTenant(Guid tenant) : ITenantContext
    {
        public Guid TenantId => tenant;
        public bool HasTenant => tenant != Guid.Empty;

        public void SetTenant(Guid id) { }
    }
}
