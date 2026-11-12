using TaxVision.Codes.Application.Definitions.SendGiftcard;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.ValueObjects;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;

namespace TaxVision.Growth.Tests.Application;

/// <summary>
/// Enviar una giftcard por correo: solo de plataforma y activa, y el código reenviado debe corresponder a ESA
/// giftcard (el backend guarda únicamente el hash). Nada se publica si algo no cuadra.
/// </summary>
public sealed class SendGiftcardHandlerTests
{
    private const string Token = "GIFT-7K3Q9M2X";

    private static (CodeDefinition Card, InMemoryCodeDefinitionRepository Repo) Setup(
        CodeOwnerScope scope = CodeOwnerScope.Platform
    )
    {
        // Un código de TENANT necesita su propio dueño y alcance; el de plataforma, no.
        var card =
            scope == CodeOwnerScope.Tenant
                ? GrowthTestData.CreateActivePercentageCode(
                    ownerTenantId: GrowthTestData.ReferrerTenantId,
                    ownerScope: scope,
                    tenantScopeId: GrowthTestData.ReferrerTenantId,
                    basisPoints: 2_000,
                    codeHashCharacter: 'a'
                )
                : GrowthTestData.CreateActivePercentageCode(basisPoints: 2_000, codeHashCharacter: 'a');
        return (card, new InMemoryCodeDefinitionRepository(card));
    }

    private static Task<BuildingBlocks.Results.Result<SendGiftcardReceipt>> Run(
        CodeDefinition card,
        InMemoryCodeDefinitionRepository repo,
        string code = Token,
        string email = "friend@example.com",
        string? message = null,
        DateTime? now = null,
        Guid? actor = null
    ) =>
        SendGiftcardHandler.Handle(
            new SendGiftcardCommand(card.Id, code, email, "Friend", message, actor ?? GrowthTestData.ActorId),
            repo,
            new MatchingHasher(card),
            new FixedTimeProvider(new DateTimeOffset(now ?? GrowthTestData.NowUtc)),
            CancellationToken.None
        );

    /// <summary>Hasher que reconoce SOLO el código de prueba como el de la tarjeta (como el hash real).</summary>
    private sealed class MatchingHasher(CodeDefinition card) : TaxVision.Codes.Application.Abstractions.ICodeTokenHasher
    {
        public BuildingBlocks.Results.Result<CodeTokenHash> Hash(string codeToken) =>
            string.Equals(codeToken.Trim(), Token, StringComparison.OrdinalIgnoreCase)
                ? BuildingBlocks.Results.Result.Success(card.CodeHash)
                : CodeTokenHash.Create(new string('f', 64));
    }

    [Fact]
    public async Task A_matching_code_builds_the_receipt_with_the_card_value()
    {
        var (card, repo) = Setup();

        var result = await Run(card, repo, message: "  Enjoy!  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("20% OFF", result.Value.ValueLabel);
        Assert.Equal(Token, result.Value.Code);
        Assert.Equal("friend@example.com", result.Value.RecipientEmail);
        Assert.Equal("Enjoy!", result.Value.PersonalMessage);
        Assert.Equal(card.ExpiresAtUtc, result.Value.ExpiresAtUtc);
    }

    [Fact]
    public async Task A_code_that_does_not_belong_to_the_card_is_rejected()
    {
        var (card, repo) = Setup();

        var result = await Run(card, repo, code: "SOMETHING-ELSE");

        Assert.Equal("Codes.SendGiftcard.CodeMismatch", result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("two@@example.com")]
    [InlineData("with space@example.com")]
    public async Task An_invalid_recipient_email_is_rejected(string email)
    {
        var (card, repo) = Setup();

        var result = await Run(card, repo, email: email);

        Assert.Equal("Codes.SendGiftcard.InvalidEmail", result.Error.Code);
    }

    [Fact]
    public async Task A_message_over_500_characters_is_rejected()
    {
        var (card, repo) = Setup();

        var result = await Run(card, repo, message: new string('m', 501));

        Assert.Equal("Codes.SendGiftcard.MessageTooLong", result.Error.Code);
    }

    [Fact]
    public async Task A_tenant_owned_code_cannot_be_sent()
    {
        var (card, repo) = Setup(CodeOwnerScope.Tenant);

        var result = await Run(card, repo);

        Assert.Equal("Codes.SendGiftcard.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task An_expired_card_cannot_be_sent()
    {
        var (card, repo) = Setup();

        var result = await Run(card, repo, now: GrowthTestData.NowUtc.AddDays(60));

        Assert.Equal("Codes.SendGiftcard.NotActive", result.Error.Code);
    }

    [Fact]
    public async Task A_revoked_card_cannot_be_sent()
    {
        var (card, repo) = Setup();
        Assert.True(card.Revoke(GrowthTestData.ActorId, GrowthTestData.NowUtc).IsSuccess);

        var result = await Run(card, repo);

        Assert.Equal("Codes.SendGiftcard.NotActive", result.Error.Code);
    }

    [Fact]
    public async Task An_unknown_card_or_missing_actor_is_rejected()
    {
        var (card, repo) = Setup();

        var unknown = await SendGiftcardHandler.Handle(
            new SendGiftcardCommand(Guid.NewGuid(), Token, "friend@example.com", null, null, GrowthTestData.ActorId),
            repo,
            new MatchingHasher(card),
            TimeProvider.System,
            CancellationToken.None
        );
        var noActor = await Run(card, repo, actor: Guid.Empty);

        Assert.Equal("Codes.SendGiftcard.NotFound", unknown.Error.Code);
        Assert.Equal("Codes.SendGiftcard.InvalidRequest", noActor.Error.Code);
    }
}
