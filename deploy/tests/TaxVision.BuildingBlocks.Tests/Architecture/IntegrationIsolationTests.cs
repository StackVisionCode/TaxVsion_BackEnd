using TaxVision.BuildingBlocks.Testing.Architecture;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Architecture;

/// <summary>
/// BB-10 sobre todas las pruebas: ninguna T-INT deja nada que la flota recoja. Lo que no aplica queda listado con su
/// motivo.
/// </summary>
public sealed class IntegrationIsolationTests
{
    private const string TestsDirectory = "deploy/tests";

    private const string Tests = TestsDirectory + "/";
    private const string NoWolverine = "No usa Wolverine ni SQL";

    private static readonly IReadOnlyDictionary<string, string> PendingAdoption = new Dictionary<string, string>(
        StringComparer.Ordinal
    )
    {
            [nameof(IntegrationIsolationKind.HostKeepsExternalTransports) + " " + Tests + "TaxVision.ContentScanner.Tests/ScanEndpointTests.cs"] = NoWolverine,
            [nameof(IntegrationIsolationKind.HostKeepsJobs) + " " + Tests + "TaxVision.ContentScanner.Tests/ScanEndpointTests.cs"] = NoWolverine,
            [nameof(IntegrationIsolationKind.HostUsesFleetEnvelopes) + " " + Tests + "TaxVision.ContentScanner.Tests/ScanEndpointTests.cs"] = NoWolverine,
    };

    [Fact]
    public void IntegrationTests_LeaveNothingForTheFleet()
    {
        var offenders = IntegrationIsolationRules
            .Scan(TestsDirectory)
            .Where(f => !PendingAdoption.ContainsKey(f.Key))
            .Select(f => f.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Pruebas sin aislar de la flota ({offenders.Length}). Base temporal, o MediatorOnly y sin jobs en un host real:\n  "
                + string.Join("\n  ", offenders)
        );
    }

    [Fact]
    public void PendingAdoption_EntriesAreStillNeeded()
    {
        var found = IntegrationIsolationRules.Scan(TestsDirectory).Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        var stale = PendingAdoption.Keys.Where(key => !found.Contains(key)).Order(StringComparer.Ordinal).ToArray();

        Assert.True(stale.Length == 0, "Entradas de PendingAdoption ya corregidas:\n  " + string.Join("\n  ", stale));
    }
}

/// <summary>BB-10. El clasificador de <see cref="IntegrationIsolationRules"/> con fuentes de ejemplo.</summary>
public sealed class IntegrationIsolationRulesTests
{
    private const string FleetSource = "new ConfigurationBuilder().AddUserSecrets(\"id\")";

    [Fact]
    public void HostOnTheFleetDatabase_WithDurabilityOn_IsReported()
    {
        var kinds = Kinds(
            """
            public sealed class ApiFactory : WebApplicationFactory<Program>
            {
                void Configure(IServiceCollection services)
                {
                    services.DisableAllExternalWolverineTransports();
                    services.Remove(services.Single(d => d.ServiceType == typeof(IHostedService)));
                }
            }
            """
        );

        Assert.Equal([IntegrationIsolationKind.HostUsesFleetEnvelopes], kinds);
    }

    [Fact]
    public void HostWithMediatorOnly_WithoutJobsAndTransports_IsFine() =>
        Assert.Empty(
            Kinds(
                """
                public sealed class ApiFactory : WebApplicationFactory<Program>
                {
                    void Configure(IServiceCollection services)
                    {
                        services.DisableAllExternalWolverineTransports();
                        options.Durability.Mode = DurabilityMode.MediatorOnly;
                        services.Remove(services.Single(d => d.ServiceType == typeof(IHostedService)));
                    }
                }
                """
            )
        );

    [Fact]
    public void BareHost_ReportsTransportsEnvelopesAndJobs() =>
        Assert.Equal(
            [
                IntegrationIsolationKind.HostKeepsExternalTransports,
                IntegrationIsolationKind.HostUsesFleetEnvelopes,
                IntegrationIsolationKind.HostKeepsJobs,
            ],
            Kinds("public sealed class ApiFactory : WebApplicationFactory<Program> { }")
        );

    [Fact]
    public void HostOnATemporaryDatabase_OnlyNeedsTheTransportsOff() =>
        Assert.Empty(
            Kinds(
                """
                public sealed class ApiFactory : WebApplicationFactory<Program>
                {
                    void Configure(IWebHostBuilder builder)
                    {
                        builder.UseSetting("ConnectionStrings:Default", connectionString);
                        services.DisableAllExternalWolverineTransports();
                    }
                }
                """
            )
        );

    [Theory]
    [InlineData("options.PersistMessagesWithSqlServer(connection);")]
    [InlineData("options.PersistMessagesWithSqlServer(connection, \"integration\");")]
    [InlineData("options.PersistMessagesWithSqlServer(connection, BillingSchemas.Integration);")]
    public void Envelopes_InTheFleetSchema_AreReported(string store) =>
        Assert.Contains(IntegrationIsolationKind.EnvelopesInFleetSchema, Kinds(FleetSource + store));

    [Fact]
    public void Envelopes_InAnOwnSchema_AreFine() =>
        Assert.DoesNotContain(
            IntegrationIsolationKind.EnvelopesInFleetSchema,
            Kinds(FleetSource + "options.PersistMessagesWithSqlServer(connection, \"own_test\");")
        );

    [Fact]
    public void EnvelopesAndRows_InATemporaryCatalog_AreFine() =>
        Assert.Empty(
            Kinds(
                FleetSource
                    + "new SqlConnectionStringBuilder(local) { InitialCatalog = name }; "
                    + "options.PersistMessagesWithSqlServer(connection); db.SaveChangesAsync();"
            )
        );

    [Fact]
    public void RowsInTheFleetDatabase_AreReported() =>
        Assert.Equal(
            [IntegrationIsolationKind.WritesToFleetDatabase],
            Kinds(FleetSource + "await db.SaveChangesAsync();")
        );

    [Theory]
    [InlineData("var database = new TemporaryTestDatabase(\"svc_tint\", local, schema);")]
    [InlineData("private static readonly TemporaryTestDatabase Database = new(\"svc_tint\", local, schema);")]
    public void TheSharedTemporaryDatabase_CountsAsTemporary(string declaration) =>
        Assert.Empty(Kinds(FleetSource + declaration + " await db.SaveChangesAsync();"));

    [Fact]
    public void ConnectionStringNamedInAnErrorMessage_IsNotATemporaryDatabase() =>
        Assert.Equal(
            [IntegrationIsolationKind.WritesToFleetDatabase],
            Kinds(FleetSource + "throw new Exception(\"Falta ConnectionStrings:Default\"); db.SaveChanges();")
        );

    [Fact]
    public void ReadOnlyQueryOnTheFleetDatabase_IsFine() =>
        Assert.Empty(Kinds(FleetSource + "await db.Set<Invoice>().CountAsync();"));

    [Fact]
    public void Helpers_TellWhichDatabaseAFileUses()
    {
        var fleet = new HashSet<string> { "OfficeTestDatabase" };
        var temporary = new HashSet<string> { "OfficeTemporaryDatabase" };

        Assert.Single(
            IntegrationIsolationRules.Classify("OfficeTestDatabase.NewContext(); db.SaveChanges();", "", temporary, fleet)
        );
        Assert.Empty(
            IntegrationIsolationRules.Classify(
                "new OfficeTemporaryDatabase(\"x\"); db.SaveChanges();",
                "",
                temporary,
                fleet
            )
        );
    }

    private static IntegrationIsolationKind[] Kinds(string source) =>
        IntegrationIsolationRules.Classify(source).Select(finding => finding.Kind).ToArray();
}
