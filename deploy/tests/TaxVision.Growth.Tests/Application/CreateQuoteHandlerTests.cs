using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Quotes.CreateQuote;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.ValueObjects;
using TaxVision.Growth.Infrastructure.Security;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;

namespace TaxVision.Growth.Tests.Application;

/// <summary>
/// La cotización de una oficina existente: Growth pone por su cuenta el alcance de la oficina compradora (sale de los
/// campos del pedido, nunca de la lista que manda el llamador) y devuelve el beneficio con su duración.
/// </summary>
public sealed class CreateQuoteHandlerTests
{
    private static readonly Guid Office = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherOffice = Guid.Parse("30000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task Implicit_tenant_target_comes_from_the_request_not_from_the_caller_list()
    {
        var definition = GrowthTestData.CreateActiveCode(
            CodeUsageContext.ExistingSubscription,
            null,
            (CodeScopeType.Tenant, Office.ToString())
        );

        // Otra oficina intenta colarse mandando el alcance de la oficina permitida en la lista.
        var forged = await QuoteAsync(
            definition,
            OtherOffice,
            [new CodeScopeTargetInput(CodeScopeType.Tenant, Office.ToString())]
        );
        var allowed = await QuoteAsync(definition, Office, []);

        Assert.Equal("Codes.CodeDefinition.ScopeNotIncluded", forged.Error.Code);
        Assert.True(allowed.IsSuccess, allowed.IsFailure ? allowed.Error.Code : null);
    }

    [Fact]
    public async Task An_existing_subscription_quote_needs_the_buyer_office()
    {
        var definition = GrowthTestData.CreateActiveCode(CodeUsageContext.ExistingSubscription);

        var quote = await QuoteAsync(definition, buyerTenantId: null, []);

        Assert.Equal("Codes.CreateQuote.BuyerTenantRequired", quote.Error.Code);
    }

    [Fact]
    public async Task Quote_returns_the_duration()
    {
        var definition = GrowthTestData.CreateActiveCode(CodeUsageContext.Both, CodeDuration.ForMonths(3).Value);

        var quote = await QuoteAsync(definition, Office, []);

        Assert.Equal(CodeBenefitType.Percentage, quote.Value.BenefitType);
        Assert.Equal(2_000, quote.Value.PercentageBasisPoints);
        Assert.Equal(CodeDurationKind.Months, quote.Value.Duration);
        Assert.Equal(3, quote.Value.DurationMonths);
        // El recibo muestra el código enmascarado, nunca en claro.
        Assert.Equal("TV...2026", quote.Value.CodeDisplay);
    }

    [Fact]
    public async Task A_code_that_does_not_stack_is_rejected_next_to_an_applied_code()
    {
        var exclusive = GrowthTestData.CreateActiveCode(CodeUsageContext.Signup);
        var applied = CodeDefinitionTests.Stackable(GrowthTestData.CreateActivePercentageCode(codeHashCharacter: 'f'));

        var alone = await SignupQuoteAsync(exclusive, [], exclusive, applied);
        var combined = await SignupQuoteAsync(exclusive, [applied.Id], exclusive, applied);

        Assert.True(alone.IsSuccess, alone.IsFailure ? alone.Error.Code : null);
        Assert.Equal("Codes.CodeDefinition.NotStackable", combined.Error.Code);
    }

    [Fact]
    public async Task Two_stackable_codes_quote_together()
    {
        var second = CodeDefinitionTests.Stackable(GrowthTestData.CreateActiveCode(CodeUsageContext.Signup));
        var applied = CodeDefinitionTests.Stackable(GrowthTestData.CreateActivePercentageCode(codeHashCharacter: 'f'));

        var combined = await SignupQuoteAsync(second, [applied.Id], second, applied);

        Assert.True(combined.IsSuccess, combined.IsFailure ? combined.Error.Code : null);
    }

    private static Task<Result<CreateQuoteResponse>> SignupQuoteAsync(
        CodeDefinition quoted,
        IReadOnlyCollection<Guid> stackedWith,
        params CodeDefinition[] stored
    ) =>
        CreateQuoteHandler.Handle(
            new CreateQuoteCommand(
                Office,
                "SAVE20",
                SubjectType.Anonymous,
                Guid.NewGuid().ToString("N"),
                "Subscription",
                "pro",
                "1",
                10_000,
                "USD",
                GrowthTestData.Sha('c'),
                $"quote-{Guid.NewGuid():N}",
                600,
                [],
                StackedWithCodeDefinitionIds: stackedWith
            ),
            new InMemoryCodeDefinitionRepository(stored),
            new InMemoryCodeQuoteRepository(),
            new InMemoryCodeUsageCounterRepository(),
            new FixedHasher(quoted.CodeHash),
            new HmacBuyerIdentityHasher(Options.Create(new CodeTokenHashingOptions { Pepper = new string('p', 48) })),
            new FileDisposableEmailDomains(),
            new FakeBusinessIdempotencyExecutor(),
            new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc)),
            CancellationToken.None
        );

    private static Task<Result<CreateQuoteResponse>> QuoteAsync(
        CodeDefinition definition,
        Guid? buyerTenantId,
        IReadOnlyCollection<CodeScopeTargetInput> targets
    ) =>
        CreateQuoteHandler.Handle(
            new CreateQuoteCommand(
                buyerTenantId ?? Office,
                "SAVE20",
                SubjectType.Tenant,
                (buyerTenantId ?? Office).ToString("N"),
                "Subscription",
                "pro",
                "v3",
                10_000,
                "USD",
                GrowthTestData.Sha('c'),
                $"quote-{Guid.NewGuid():N}",
                600,
                targets,
                Context: CodeUsageContext.ExistingSubscription,
                BuyerTenantId: buyerTenantId
            ),
            new InMemoryCodeDefinitionRepository(definition),
            new InMemoryCodeQuoteRepository(),
            new InMemoryCodeUsageCounterRepository(),
            new FixedHasher(definition.CodeHash),
            new HmacBuyerIdentityHasher(Options.Create(new CodeTokenHashingOptions { Pepper = new string('p', 48) })),
            new FileDisposableEmailDomains(),
            new FakeBusinessIdempotencyExecutor(),
            new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc)),
            CancellationToken.None
        );

    private sealed class FixedHasher(CodeTokenHash hash) : ICodeTokenHasher
    {
        public Result<CodeTokenHash> Hash(string codeToken) => Result.Success(hash);
    }
}
