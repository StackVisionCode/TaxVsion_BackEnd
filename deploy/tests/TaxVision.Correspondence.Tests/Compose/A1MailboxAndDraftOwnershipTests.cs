using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Tests.Projections;
using Xunit;

namespace TaxVision.Correspondence.Tests.Compose;

/// <summary>
/// A1 — dos agujeros del mismo tamaño en Correspondence:
///
/// <list type="number">
/// <item>El <c>AccountId</c> llegaba en el cuerpo del request y nadie lo validaba: un empleado podía
/// redactar —y después enviar— <b>desde el buzón personal de un colega</b>, que es exactamente lo que el
/// gate de buzón ya impedía del lado de la lectura.</item>
/// <item>Un borrador es un correo a medio escribir, y se leía y listaba sin filtrar por autor: el de
/// cualquiera estaba a un id de distancia.</item>
/// </list>
/// </summary>
public sealed class A1MailboxAndDraftOwnershipTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid OwnMailbox = Guid.NewGuid();
    private static readonly Guid ColleagueMailbox = Guid.NewGuid();
    private static readonly Guid Author = Guid.NewGuid();
    private static readonly Guid Colleague = Guid.NewGuid();

    // ---------- buzón de envío ----------

    [Fact]
    public void Composing_from_your_own_mailbox_is_allowed()
    {
        Assert.True(SendingAccountGuard.Validate(OwnMailbox, [OwnMailbox]).IsSuccess);
    }

    [Fact]
    public void Composing_from_a_colleagues_mailbox_is_rejected()
    {
        var result = SendingAccountGuard.Validate(ColleagueMailbox, [OwnMailbox]);

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.AccountNotVisible", result.Error.Code);
    }

    /// <summary>Quien ve el buzón de la oficina (visible == null) redacta desde cualquiera.</summary>
    [Fact]
    public void With_office_access_any_mailbox_goes()
    {
        Assert.True(SendingAccountGuard.Validate(ColleagueMailbox, null).IsSuccess);
    }

    /// <summary>
    /// Fail-closed: si Connectors no responde, el resolver devuelve un set VACÍO. Eso tiene que impedir
    /// redactar, no abrir todos los buzones.
    /// </summary>
    [Fact]
    public void An_empty_visible_set_composes_from_nowhere()
    {
        Assert.True(SendingAccountGuard.Validate(OwnMailbox, []).IsFailure);
    }

    [Fact]
    public async Task Creating_a_draft_from_a_colleagues_mailbox_fails()
    {
        var drafts = new FakeDraftRepository();

        var result = await CreateDraftHandler.Handle(
            new CreateDraftCommand(TenantId, CustomerId, ColleagueMailbox, Author, [OwnMailbox]),
            drafts,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.AccountNotVisible", result.Error.Code);
    }

    [Fact]
    public async Task Creating_a_draft_from_your_own_mailbox_works()
    {
        var drafts = new FakeDraftRepository();

        var result = await CreateDraftHandler.Handle(
            new CreateDraftCommand(TenantId, CustomerId, OwnMailbox, Author, [OwnMailbox]),
            drafts,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    // ---------- lectura de borradores ----------

    [Fact]
    public async Task A_colleagues_draft_reads_as_not_found()
    {
        var drafts = new FakeDraftRepository();
        var draftId = await SeedDraftAsync(drafts, Author);

        var result = await GetDraftHandler.Handle(
            new GetDraftQuery(TenantId, draftId, Colleague, CanReadOtherUsersDrafts: false),
            drafts,
            CancellationToken.None
        );

        // 404 y no 403: un 403 le confirmaría que ese id existe y sobre qué cliente se está escribiendo.
        Assert.True(result.IsFailure);
        Assert.Equal("Draft.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Your_own_draft_reads_fine()
    {
        var drafts = new FakeDraftRepository();
        var draftId = await SeedDraftAsync(drafts, Author);

        var result = await GetDraftHandler.Handle(
            new GetDraftQuery(TenantId, draftId, Author, CanReadOtherUsersDrafts: false),
            drafts,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public async Task With_office_access_a_colleagues_draft_reads_fine()
    {
        var drafts = new FakeDraftRepository();
        var draftId = await SeedDraftAsync(drafts, Author);

        var result = await GetDraftHandler.Handle(
            new GetDraftQuery(TenantId, draftId, Colleague, CanReadOtherUsersDrafts: true),
            drafts,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task The_listing_only_shows_your_own_drafts()
    {
        var drafts = new FakeDraftRepository();
        await SeedDraftAsync(drafts, Author);
        await SeedDraftAsync(drafts, Colleague);

        var mine = await ListDraftsHandler.Handle(
            new ListDraftsQuery(TenantId, CustomerId, 1, 20, Author),
            drafts,
            CancellationToken.None
        );
        var everything = await ListDraftsHandler.Handle(
            new ListDraftsQuery(TenantId, CustomerId, 1, 20, OwnerUserId: null),
            drafts,
            CancellationToken.None
        );

        // El total también tiene que reflejar el filtro: filtrar después de paginar dejaría los
        // contadores mintiendo.
        Assert.Equal(1, mine.TotalCount);
        Assert.Single(mine.Items);
        Assert.Equal(2, everything.TotalCount);
    }

    private static async Task<Guid> SeedDraftAsync(FakeDraftRepository drafts, Guid author)
    {
        var result = await CreateDraftHandler.Handle(
            new CreateDraftCommand(TenantId, CustomerId, OwnMailbox, author),
            drafts,
            new FakeUnitOfWork(),
            CancellationToken.None
        );
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }
}
