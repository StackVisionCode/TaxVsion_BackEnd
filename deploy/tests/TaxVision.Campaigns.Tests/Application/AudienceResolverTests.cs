using TaxVision.Campaigns.Application.Runs.Audience;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Contacts;
using TaxVision.Campaigns.Tests.Fakes;

namespace TaxVision.Campaigns.Tests.Application;

public sealed class AudienceResolverTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private const CampaignChannel EmailSms = CampaignChannel.Email | CampaignChannel.Sms;

    private sealed class Harness
    {
        public FakeContactRepository Contacts { get; } = new();
        public FakeContactListRepository Lists { get; } = new();
        public ContactList List { get; }

        public Harness()
        {
            List = ContactList.Create(Tenant, "L", null).Value;
            Lists.Seed(List);
        }

        public Contact AddContact(string? email, string? phone)
        {
            var c = Contact.Create(Tenant, null, email, phone, ContactSource.Manual).Value;
            Contacts.Seed(c);
            List.AddMember(c.Id);
            return c;
        }
    }

    [Fact]
    public async Task Resolves_one_unit_per_contact_and_channel_with_destination()
    {
        var h = new Harness();
        h.AddContact("ana@x.com", null); // Email only
        h.AddContact("beto@x.com", "+18290000001"); // Email + Sms

        var units = await AudienceResolver.ResolveAsync(Tenant, EmailSms, [h.List.Id], [], h.Contacts, h.Lists);

        Assert.Equal(3, units.Count);
        Assert.Equal(2, units.Count(u => u.Channel == CampaignChannel.Email));
        Assert.Equal(1, units.Count(u => u.Channel == CampaignChannel.Sms));
    }

    [Fact]
    public async Task Excludes_channel_the_contact_opted_out_of()
    {
        var h = new Harness();
        h.AddContact("ana@x.com", null);
        var beto = h.AddContact("beto@x.com", "+18290000001");
        beto.OptOut(CampaignChannel.Email);

        var units = await AudienceResolver.ResolveAsync(Tenant, EmailSms, [h.List.Id], [], h.Contacts, h.Lists);

        // Ana Email + Beto Sms (Beto Email excluido por opt-out)
        Assert.Equal(2, units.Count);
        Assert.DoesNotContain(units, u => u.Channel == CampaignChannel.Email && u.Email == "beto@x.com");
    }

    [Fact]
    public async Task Dedupes_by_channel_and_destination()
    {
        var h = new Harness();
        h.AddContact("dup@x.com", null);
        h.AddContact("dup@x.com", null); // mismo destino → una sola unidad Email

        var units = await AudienceResolver.ResolveAsync(
            Tenant,
            CampaignChannel.Email,
            [h.List.Id],
            [],
            h.Contacts,
            h.Lists
        );

        Assert.Single(units);
    }

    [Fact]
    public async Task Includes_manual_entries()
    {
        var h = new Harness();
        h.AddContact("ana@x.com", null);

        var units = await AudienceResolver.ResolveAsync(
            Tenant,
            CampaignChannel.Email,
            [h.List.Id],
            [new ManualAudienceEntry("manual@x.com", null)],
            h.Contacts,
            h.Lists
        );

        Assert.Equal(2, units.Count);
        Assert.Contains(units, u => u.Email == "manual@x.com");
    }

    [Fact]
    public async Task Empty_audience_resolves_to_no_units()
    {
        var h = new Harness();
        var units = await AudienceResolver.ResolveAsync(Tenant, EmailSms, [], [], h.Contacts, h.Lists);
        Assert.Empty(units);
    }

    [Fact]
    public async Task Includes_customers_when_requested_and_dedupes_against_lists()
    {
        var h = new Harness();
        h.AddContact("ana@x.com", null); // en lista
        var customers = new FakeCustomerAudienceClient();
        customers.Seed("ana@x.com", null); // mismo destino que Ana → dedupe
        customers.Seed("cliente@x.com", "+18290000009"); // cliente nuevo (email + phone)

        var units = await AudienceResolver.ResolveAsync(
            Tenant,
            EmailSms,
            [h.List.Id],
            [],
            h.Contacts,
            h.Lists,
            includeCustomers: true,
            customerClient: customers
        );

        // Ana Email (lista) + cliente Email + cliente Sms = 3 (ana@ del cliente deduplicada)
        Assert.Equal(3, units.Count);
        Assert.Equal(2, units.Count(u => u.Channel == CampaignChannel.Email));
        Assert.Equal(1, units.Count(u => u.Channel == CampaignChannel.Sms));
    }

    [Fact]
    public async Task Does_not_call_customers_when_flag_off()
    {
        var h = new Harness();
        h.AddContact("ana@x.com", null);
        var units = await AudienceResolver.ResolveAsync(
            Tenant,
            EmailSms,
            [h.List.Id],
            [],
            h.Contacts,
            h.Lists,
            includeCustomers: false,
            customerClient: new FakeCustomerAudienceClient()
        );
        Assert.Single(units); // solo Ana Email
    }

    [Fact]
    public async Task Restricts_customers_to_the_assigned_set_P2()
    {
        var h = new Harness();
        var customers = new FakeCustomerAudienceClient();
        customers.Seed("assigned@x.com", null); // asignado al actor
        customers.Seed("other@x.com", null); // NO asignado
        var assignedCustomerId = customers.Members[0].CustomerId;

        var units = await AudienceResolver.ResolveAsync(
            Tenant,
            CampaignChannel.Email,
            [],
            [],
            h.Contacts,
            h.Lists,
            includeCustomers: true,
            customerClient: customers,
            restrictCustomerIds: new HashSet<Guid> { assignedCustomerId }
        );

        // Solo el cliente asignado entra en la audiencia; el otro se excluye.
        Assert.Single(units);
        Assert.Equal("assigned@x.com", units[0].Email);
    }

    [Fact]
    public async Task Empty_restrict_set_yields_no_customers_P2()
    {
        var h = new Harness();
        var customers = new FakeCustomerAudienceClient();
        customers.Seed("a@x.com", null);
        customers.Seed("b@x.com", null);

        var units = await AudienceResolver.ResolveAsync(
            Tenant,
            CampaignChannel.Email,
            [],
            [],
            h.Contacts,
            h.Lists,
            includeCustomers: true,
            customerClient: customers,
            restrictCustomerIds: new HashSet<Guid>()
        );

        // Actor sin clientes asignados → ningún cliente en la audiencia.
        Assert.Empty(units);
    }

    [Fact]
    public async Task Null_restrict_set_includes_all_customers_P2()
    {
        var h = new Harness();
        var customers = new FakeCustomerAudienceClient();
        customers.Seed("a@x.com", null);
        customers.Seed("b@x.com", null);

        var units = await AudienceResolver.ResolveAsync(
            Tenant,
            CampaignChannel.Email,
            [],
            [],
            h.Contacts,
            h.Lists,
            includeCustomers: true,
            customerClient: customers,
            restrictCustomerIds: null
        );

        // Sin restricción (flag off / view_all / agendado) → todos los clientes activos.
        Assert.Equal(2, units.Count);
    }
}
