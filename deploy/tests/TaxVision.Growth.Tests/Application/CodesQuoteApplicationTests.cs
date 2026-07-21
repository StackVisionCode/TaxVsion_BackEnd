using System.Text.Json;
using BuildingBlocks.Tenancy;
using TaxVision.Codes.Application.Quotes.CreateQuote;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.ValueObjects;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;

namespace TaxVision.Growth.Tests.Application;

public sealed class CodesQuoteApplicationTests
{
    private const string PlaintextToken = "PRIVATE-CODE-1234";

    [Fact]
    public async Task CreateQuote_redacts_token_and_only_passes_it_to_the_hasher()
    {
        var expectedHash = SpyCodeTokenHasher.HashWithoutObservation(PlaintextToken);
        var definition = CreateActiveCode(expectedHash);
        var definitions = new InMemoryCodeDefinitionRepository(definition);
        var quotes = new InMemoryCodeQuoteRepository();
        var hasher = new SpyCodeTokenHasher();
        var idempotency = new FakeBusinessIdempotencyExecutor();
        var clock = new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc));
        var command = CreateCommand();

        var result = await CreateQuoteHandler.Handle(
            command,
            definitions,
            quotes,
            hasher,
            idempotency,
            clock,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal([PlaintextToken], hasher.ReceivedTokens);
        Assert.Equal(expectedHash, definitions.LastApplicableHash);
        Assert.DoesNotContain(PlaintextToken, command.ToString(), StringComparison.Ordinal);
        Assert.Contains("<redacted>", command.ToString(), StringComparison.Ordinal);

        var responseJson = JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain(PlaintextToken, responseJson, StringComparison.Ordinal);
        Assert.DoesNotContain(expectedHash.Value, responseJson, StringComparison.Ordinal);
        Assert.Single(quotes.Quotes);
    }

    [Fact]
    public async Task Business_idempotency_replays_equal_payload_and_conflicts_on_different_payload()
    {
        var definition = CreateActiveCode(SpyCodeTokenHasher.HashWithoutObservation(PlaintextToken));
        var definitions = new InMemoryCodeDefinitionRepository(definition);
        var quotes = new InMemoryCodeQuoteRepository();
        var hasher = new SpyCodeTokenHasher();
        var idempotency = new FakeBusinessIdempotencyExecutor();
        var clock = new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc));
        var command = CreateCommand();

        var first = await CreateQuoteHandler.Handle(
            command,
            definitions,
            quotes,
            hasher,
            idempotency,
            clock,
            CancellationToken.None
        );
        var replay = await CreateQuoteHandler.Handle(
            command,
            definitions,
            quotes,
            hasher,
            idempotency,
            clock,
            CancellationToken.None
        );
        var conflict = await CreateQuoteHandler.Handle(
            command with
            {
                GrossAmountCents = command.GrossAmountCents + 1,
            },
            definitions,
            quotes,
            hasher,
            idempotency,
            clock,
            CancellationToken.None
        );

        Assert.True(first.IsSuccess);
        Assert.True(replay.IsSuccess);
        Assert.Equal(first.Value.QuoteId, replay.Value.QuoteId);
        Assert.True(conflict.IsFailure);
        Assert.Equal("Codes.Idempotency.Conflict", conflict.Error.Code);
        Assert.Equal(1, idempotency.ExecutedBodyCount);
        Assert.Single(quotes.Quotes);
    }

    private static CreateQuoteCommand CreateCommand() =>
        new(
            GrowthTestData.RefereeTenantId,
            PlaintextToken,
            SubjectType.Tenant,
            "tenant-subject-1",
            "Subscription",
            "pro",
            "v3",
            10_000,
            "USD",
            GrowthTestData.Sha('c'),
            "quote-operation-1",
            600
        );

    private static CodeDefinition CreateActiveCode(CodeTokenHash hash)
    {
        var definition = CodeDefinition
            .Create(
                PlatformTenant.Id,
                CodeOwnerScope.Platform,
                tenantScopeId: null,
                "Quote security code",
                CodeKind.Promotional,
                hash,
                CodeDisplay.Create("PRIV", "1234").Value,
                GrowthTestData.NowUtc.AddDays(-1),
                GrowthTestData.NowUtc.AddDays(30),
                100,
                10,
                2,
                GrowthTestData.ActorId,
                GrowthTestData.NowUtc
            )
            .Value;
        definition.PublishRuleVersion(
            CodeBenefit.CreatePercentage(PercentageBasisPoints.Create(1_000).Value).Value,
            minimumPurchase: null,
            allowStacking: false,
            GrowthTestData.ActorId,
            GrowthTestData.NowUtc
        );
        Assert.True(definition.Activate(GrowthTestData.ActorId, GrowthTestData.NowUtc).IsSuccess);
        return definition;
    }
}
