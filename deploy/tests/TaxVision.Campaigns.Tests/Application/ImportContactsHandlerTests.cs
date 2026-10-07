using TaxVision.Campaigns.Application.Contacts.Abstractions;
using TaxVision.Campaigns.Application.Contacts.Commands;
using TaxVision.Campaigns.Domain.Contacts;
using TaxVision.Campaigns.Tests.Fakes;

namespace TaxVision.Campaigns.Tests.Application;

public sealed class ImportContactsHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    // Fake neutro: devuelve AlreadyExisted — no afecta los contadores que estos tests chequean
    // (Created/Reused/Invalid/MembersAdded). Solo evita el null-ref en el handler.
    private sealed class FakeCustomerDirectoryClient : ICustomerDirectoryClient
    {
        public Task<CustomerProvisionResult> CreateIndividualAsync(
            Guid tenantId,
            string? callerBearerToken,
            string? name,
            string? email,
            string? phoneE164,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                string.IsNullOrWhiteSpace(email)
                    ? new CustomerProvisionResult(CustomerProvisionOutcome.SkippedNoEmail)
                    : new CustomerProvisionResult(CustomerProvisionOutcome.AlreadyExisted)
            );
    }

    private sealed class Harness
    {
        public FakeContactRepository Contacts { get; } = new();
        public FakeContactListRepository Lists { get; } = new();
        public FakeCustomerDirectoryClient Customers { get; } = new();
        public FakeUnitOfWork Uow { get; } = new();
        public ContactList List { get; }

        public Harness()
        {
            List = ContactList.Create(Tenant, "Clientes", null).Value;
            Lists.Seed(List);
        }
    }

    [Fact]
    public async Task Import_dedupes_in_batch_and_skips_invalid_rows()
    {
        var h = new Harness();
        var csv = "name,email,phone\nAna,ana@example.com,\nBeto,beto@example.com,\nAna,ANA@example.com,\nSinDatos,,\n";

        var result = await ImportContactsHandler.Handle(
            new ImportContactsCommand(Tenant, h.List.Id, csv),
            h.Lists,
            h.Contacts,
            h.Customers,
            h.Uow,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Created); // Ana, Beto
        Assert.Equal(1, result.Value.Reused); // duplicate ANA@ (case-insensitive)
        Assert.Equal(1, result.Value.Invalid); // SinDatos (no destination)
        Assert.Equal(2, result.Value.MembersAdded);
        Assert.Equal(2, h.List.Members.Count);
    }

    [Fact]
    public async Task Import_reuses_existing_contact_by_destination()
    {
        var h = new Harness();
        h.Contacts.Seed(Contact.Create(Tenant, "Ana", "ana@example.com", null, ContactSource.Manual).Value);

        // email en su columna (name,email,phone) — un solo token iría a "name".
        var result = await ImportContactsHandler.Handle(
            new ImportContactsCommand(Tenant, h.List.Id, ",ana@example.com,\n"),
            h.Lists,
            h.Contacts,
            h.Customers,
            h.Uow,
            CancellationToken.None
        );

        Assert.Equal(0, result.Value.Created);
        Assert.Equal(1, result.Value.Reused);
        Assert.Equal(1, result.Value.MembersAdded);
    }

    [Fact]
    public async Task Import_into_unknown_list_fails()
    {
        var h = new Harness();
        var result = await ImportContactsHandler.Handle(
            new ImportContactsCommand(Tenant, Guid.NewGuid(), "a@x.com\n"),
            h.Lists,
            h.Contacts,
            h.Customers,
            h.Uow,
            CancellationToken.None
        );
        Assert.True(result.IsFailure);
    }
}
