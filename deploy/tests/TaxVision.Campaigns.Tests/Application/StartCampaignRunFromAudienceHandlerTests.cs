using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Campaigns.Application.Runs;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Application.Runs.Audience;
using TaxVision.Campaigns.Application.Runs.Commands;
using TaxVision.Campaigns.Application.Runs.Queries;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Contacts;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Tests.Fakes;

namespace TaxVision.Campaigns.Tests.Application;

/// <summary>Arranque y vista previa desde audiencia: listas, manual y fuente "Clients" con su visibilidad.</summary>
public sealed class StartCampaignRunFromAudienceHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    private readonly FakeCampaignRepository _campaigns = new();
    private readonly FakeCampaignRunRepository _runs = new();
    private readonly FakeCampaignScheduleRepository _schedules = new();
    private readonly FakeContactRepository _contacts = new();
    private readonly FakeContactListRepository _lists = new();
    private readonly FakeCustomerAudienceClient _customers = new();
    private readonly FakeWalletSpendClient _wallet = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeMessageBus _bus = new();
    private readonly Campaign _campaign;
    private readonly ContactList _list;
    private bool _visibilityEnabled;

    public StartCampaignRunFromAudienceHandlerTests()
    {
        _campaign = Campaign.Create(Tenant, Actor, "Newsletter", CampaignChannel.Email, "body", "subj").Value;
        _campaigns.Campaigns.Add(_campaign);
        _list = ContactList.Create(Tenant, "L", null).Value;
        _lists.Seed(_list);
        TestRunFlow.Route(_bus, _runs, _campaigns, _schedules, _unitOfWork);
    }

    private void AddContact(string email)
    {
        var contact = Contact.Create(Tenant, null, email, null, ContactSource.Manual).Value;
        _contacts.Seed(contact);
        _list.AddMember(contact.Id);
    }

    private StartCampaignRunFromAudienceCommand Command(
        IReadOnlyList<ManualAudienceEntry>? manual = null,
        bool includeCustomers = false,
        bool canViewAll = true,
        Guid? campaignId = null,
        string triggerKind = "Manual",
        string? bearer = null
    ) =>
        new(
            Tenant,
            campaignId ?? _campaign.Id,
            Actor,
            [_list.Id],
            manual ?? [],
            triggerKind,
            includeCustomers,
            CanViewAllCustomers: canViewAll,
            CallerBearerToken: bearer
        );

    private Task<Result<CampaignRunResponse>> StartAsync(StartCampaignRunFromAudienceCommand command) =>
        StartCampaignRunFromAudienceHandler.Handle(
            command,
            _campaigns,
            Audience(),
            new CampaignRunStarter(_wallet, new FakeCorrelationContext(), NullLogger<CampaignRunStarter>.Instance),
            _bus,
            CancellationToken.None
        );

    private Task<Result<AudiencePreviewResponse>> PreviewAsync(PreviewAudienceQuery query) =>
        PreviewAudienceHandler.Handle(query, _campaigns, Audience(), CancellationToken.None);

    private CampaignAudienceService Audience() =>
        new(
            _contacts,
            _lists,
            _customers,
            new FakeCampaignCustomerAssignmentReader(),
            Options.Create(new CampaignsVisibilityOptions { Enabled = _visibilityEnabled })
        );

    [Fact]
    public async Task Unknown_campaign_is_rejected_without_a_run()
    {
        var result = await StartAsync(Command(campaignId: Guid.NewGuid()));

        Assert.Equal(CampaignErrors.NotFound, result.Error);
        Assert.Empty(_runs.Runs);
    }

    [Fact]
    public async Task Archived_campaign_is_rejected_without_a_run()
    {
        _campaign.Archive();

        var result = await StartAsync(Command());

        Assert.Equal(CampaignErrors.Archived, result.Error);
        Assert.Empty(_runs.Runs);
    }

    [Fact]
    public async Task List_and_manual_entries_become_one_run_with_one_dispatch_per_unit()
    {
        AddContact("ana@x.com");

        var result = await StartAsync(Command(manual: [new ManualAudienceEntry("beto@x.com", null)], bearer: "token"));

        Assert.True(result.IsSuccess);
        var run = Assert.Single(_runs.Runs);
        Assert.Equal("Manual", run.TriggerKind);
        Assert.Equal(Actor, run.TriggeredByUserId);
        Assert.Equal(CampaignRunStatus.Dispatching, run.Status);
        Assert.Equal([run.Id], _wallet.ReservedReferences);
        Assert.Equal("token", _wallet.LastCallerBearerToken);
        Assert.Equal(2, _bus.OfType<CampaignDispatchRequestedIntegrationEvent>().Count());
    }

    [Fact]
    public async Task The_trigger_kind_of_the_command_reaches_the_run()
    {
        AddContact("ana@x.com");

        await StartAsync(Command(triggerKind: "Api"));

        Assert.Equal("Api", Assert.Single(_runs.Runs).TriggerKind);
    }

    [Fact]
    public async Task An_actor_who_sees_all_reaches_every_active_client()
    {
        _visibilityEnabled = true;
        _customers.Seed("cli@x.com", null);

        await StartAsync(Command(includeCustomers: true));

        Assert.Equal(1, Assert.Single(_runs.Runs).RecipientCount);
    }

    [Fact]
    public async Task With_visibility_on_an_actor_only_reaches_assigned_clients()
    {
        _visibilityEnabled = true;
        AddContact("ana@x.com");
        _customers.Seed("cli@x.com", null);

        await StartAsync(Command(includeCustomers: true, canViewAll: false));

        Assert.Equal(1, Assert.Single(_runs.Runs).RecipientCount);
        Assert.Equal("ana@x.com", Assert.Single(Assert.Single(_runs.Runs).Recipients).Email);
    }

    [Fact]
    public async Task An_incomplete_directory_stops_the_run_before_the_wallet()
    {
        _customers.IsComplete = false;

        var result = await StartAsync(Command(includeCustomers: true));

        Assert.Equal(CampaignRunErrors.AudienceIncomplete, result.Error);
        Assert.Empty(_runs.Runs);
        Assert.Empty(_wallet.ReservedReferences);
    }

    [Fact]
    public async Task Preview_counts_recipients_and_units_without_opening_a_run()
    {
        AddContact("ana@x.com");
        _customers.Seed("cli@x.com", null);

        var preview = await PreviewAsync(new PreviewAudienceQuery(Tenant, _campaign.Id, [_list.Id], [], true));

        Assert.Equal(2, preview.Value.RecipientCount);
        Assert.Equal(2, preview.Value.Email);
        Assert.Empty(_runs.Runs);
    }

    [Fact]
    public async Task Preview_applies_the_same_visibility_as_the_run()
    {
        _visibilityEnabled = true;
        _customers.Seed("cli@x.com", null);

        var preview = await PreviewAsync(
            new PreviewAudienceQuery(
                Tenant,
                _campaign.Id,
                [],
                [],
                IncludeCustomers: true,
                CanViewAllCustomers: false,
                TriggeredByUserId: Actor
            )
        );

        Assert.Equal(0, preview.Value.RecipientCount);
    }

    [Fact]
    public async Task Preview_of_an_unknown_campaign_fails()
    {
        var preview = await PreviewAsync(new PreviewAudienceQuery(Tenant, Guid.NewGuid(), [], []));

        Assert.Equal(CampaignErrors.NotFound, preview.Error);
    }
}
