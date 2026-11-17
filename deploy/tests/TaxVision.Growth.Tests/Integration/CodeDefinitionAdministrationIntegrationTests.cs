using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Codes.Application.Definitions.ExpireCode;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.ValueObjects;
using TaxVision.Growth.Infrastructure.Persistence;
using TaxVision.Growth.Infrastructure.Persistence.Repositories.Codes;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;

namespace TaxVision.Growth.Tests.Integration;

/// <summary>
/// Contra SQL Server, en una base temporal propia: el barrido encuentra los códigos vencidos y los cierra de a uno
/// (cada uno se guarda solo); reemplazar alcances y publicar una regla sobre un código guardado insertan filas nuevas.
/// </summary>
public sealed class CodeDefinitionAdministrationIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly GrowthTemporaryDatabase _database = new();
    private static readonly DateTime LongAgo = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private GrowthDbContext NewContext() => _database.NewContext(_tenantId);

    private CodeDefinitionRepository Repository(GrowthDbContext db) => new(db, new FixedTenant(_tenantId));

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task The_sweep_finds_overdue_codes_and_expires_each_one_in_its_own_unit()
    {
        var first = await SeedAsync('1', LongAgo.AddDays(1));
        var second = await SeedAsync('2', LongAgo.AddDays(2));
        var current = await SeedAsync('3', DateTime.UtcNow.AddDays(30));

        await using (var db = NewContext())
        {
            var due = await Repository(db).GetExpiredOpenAsync(DateTime.UtcNow, 200);
            Assert.Contains(due, item => item.CodeDefinitionId == first && item.TenantId == _tenantId);
            Assert.Contains(due, item => item.CodeDefinitionId == second);
            Assert.DoesNotContain(due, item => item.CodeDefinitionId == current);
        }

        await ExpireAsync(first);
        Assert.Equal(CodeDefinitionStatus.Expired, await StatusAsync(first));
        Assert.Equal(CodeDefinitionStatus.Draft, await StatusAsync(second));

        await ExpireAsync(second);
        Assert.Equal(CodeDefinitionStatus.Expired, await StatusAsync(second));
        Assert.Equal(CodeDefinitionStatus.Draft, await StatusAsync(current));
    }

    [Fact]
    public async Task Replacing_scopes_deletes_the_old_rows()
    {
        var id = await SeedAsync('4', DateTime.UtcNow.AddDays(30), (CodeScopeType.Plan, "plan-old"));

        await using (var db = NewContext())
        {
            var definition = (await Repository(db).GetOwnedByIdAsync(_tenantId, id))!;
            Assert.True(
                definition
                    .ReplaceScopes(
                        [(CodeScopeType.Offer, "Subscription/plan-new/Yearly", CodeScopeMode.Include)],
                        GrowthTestData.ActorId,
                        DateTime.UtcNow
                    )
                    .IsSuccess
            );
            await db.SaveChangesAsync();
        }

        await using var verify = NewContext();
        var stored = (await Repository(verify).GetOwnedByIdAsync(_tenantId, id))!;
        Assert.Equal("Subscription/plan-new/Yearly", Assert.Single(stored.Scopes).ScopeId);
    }

    [Fact]
    public async Task Publishing_a_rule_on_a_saved_code_inserts_the_new_version()
    {
        var id = await SeedAsync('5', DateTime.UtcNow.AddDays(30));

        await using (var db = NewContext())
        {
            var definition = (await Repository(db).GetOwnedByIdAsync(_tenantId, id))!;
            Assert.True(
                definition
                    .PublishRuleVersion(
                        CodeBenefit.CreatePercentage(PercentageBasisPoints.Create(1_500).Value).Value,
                        null,
                        allowStacking: true,
                        GrowthTestData.ActorId,
                        DateTime.UtcNow
                    )
                    .IsSuccess
            );
            await db.SaveChangesAsync();
        }

        await using var verify = NewContext();
        var stored = (await Repository(verify).GetOwnedByIdAsync(_tenantId, id))!;
        Assert.Equal([1, 2], stored.RuleVersions.Select(rule => rule.Version).Order());
    }

    private async Task ExpireAsync(Guid id)
    {
        await using var db = NewContext();
        var result = await ExpireCodeHandler.Handle(
            new ExpireCodeCommand(_tenantId, id),
            Repository(db),
            new FixedTimeProvider(DateTimeOffset.UtcNow),
            CancellationToken.None
        );
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        await db.SaveChangesAsync();
    }

    private async Task<CodeDefinitionStatus> StatusAsync(Guid id)
    {
        await using var db = NewContext();
        return await db.CodeDefinitions.IgnoreQueryFilters().Where(d => d.Id == id).Select(d => d.Status).SingleAsync();
    }

    private async Task<Guid> SeedAsync(char hash, DateTime expiresAtUtc, params (CodeScopeType, string)[] includes)
    {
        var definition = CodeDefinition
            .Create(
                _tenantId,
                CodeOwnerScope.Tenant,
                _tenantId,
                "Expiry sweep",
                CodeKind.Promotional,
                CodeTokenHash.Create(new string(hash, 64)).Value,
                CodeDisplay.Create("TV", "SWEP").Value,
                expiresAtUtc.AddDays(-10),
                expiresAtUtc,
                null,
                null,
                null,
                GrowthTestData.ActorId,
                DateTime.UtcNow
            )
            .Value;
        Assert.True(
            definition
                .PublishRuleVersion(
                    CodeBenefit.CreatePercentage(PercentageBasisPoints.Create(1_000).Value).Value,
                    null,
                    allowStacking: false,
                    GrowthTestData.ActorId,
                    DateTime.UtcNow
                )
                .IsSuccess
        );
        foreach (var (type, scopeId) in includes)
            Assert.True(
                definition
                    .AddScope(type, scopeId, CodeScopeMode.Include, GrowthTestData.ActorId, DateTime.UtcNow)
                    .IsSuccess
            );

        await using var db = NewContext();
        await Repository(db).AddAsync(definition);
        await db.SaveChangesAsync();
        return definition.Id;
    }
}
