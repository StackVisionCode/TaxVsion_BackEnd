using BuildingBlocks.Common;
using TaxVision.Campaigns.Application.Contacts.Abstractions;
using TaxVision.Campaigns.Domain.Contacts;

namespace TaxVision.Campaigns.Tests.Fakes;

internal sealed class FakeContactRepository : IContactRepository
{
    public List<Contact> Store { get; } = [];

    public void Seed(Contact c) => Store.Add(c);

    public Task<Contact?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Store.FirstOrDefault(c => c.TenantId == tenantId && c.Id == id));

    public Task<Contact?> FindByDestinationAsync(
        Guid tenantId,
        string? email,
        string? phoneE164,
        CancellationToken ct = default
    )
    {
        var e = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        var p = string.IsNullOrWhiteSpace(phoneE164) ? null : phoneE164.Trim();
        if (e is null && p is null)
            return Task.FromResult<Contact?>(null);
        return Task.FromResult(
            Store.FirstOrDefault(c =>
                c.TenantId == tenantId && ((e != null && c.Email == e) || (p != null && c.PhoneE164 == p))
            )
        );
    }

    public Task<IReadOnlyList<Contact>> GetManyByIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct = default
    )
    {
        IReadOnlyList<Contact> list = Store.Where(c => c.TenantId == tenantId && ids.Contains(c.Id)).ToList();
        return Task.FromResult(list);
    }

    public Task<PagedResult<Contact>> ListAsync(Guid tenantId, int page, int size, CancellationToken ct = default)
    {
        var all = Store.Where(c => c.TenantId == tenantId).ToList();
        IReadOnlyList<Contact> items = all.Skip((page - 1) * size).Take(size).ToList();
        return Task.FromResult(new PagedResult<Contact>(items, page, size, all.Count));
    }

    public Task AddAsync(Contact contact, CancellationToken ct = default)
    {
        Store.Add(contact);
        return Task.CompletedTask;
    }
}

internal sealed class FakeCustomerAudienceClient : ICustomerAudienceClient
{
    public List<CustomerAudienceMember> Members { get; } = [];

    public void Seed(string? email, string? phone) =>
        Members.Add(new CustomerAudienceMember(Guid.NewGuid(), email, phone));

    public Task<IReadOnlyList<CustomerAudienceMember>> GetActiveCustomersAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<CustomerAudienceMember>>(Members);
}

internal sealed class FakeContactListRepository : IContactListRepository
{
    public List<ContactList> Store { get; } = [];

    public void Seed(ContactList l) => Store.Add(l);

    public Task<ContactList?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Store.FirstOrDefault(l => l.TenantId == tenantId && l.Id == id));

    public Task<IReadOnlyList<Guid>> GetMemberContactIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> listIds,
        CancellationToken ct = default
    )
    {
        IReadOnlyList<Guid> ids = Store
            .Where(l => l.TenantId == tenantId && listIds.Contains(l.Id))
            .SelectMany(l => l.Members.Select(m => m.ContactId))
            .Distinct()
            .ToList();
        return Task.FromResult(ids);
    }

    public Task<PagedResult<ContactList>> ListAsync(Guid tenantId, int page, int size, CancellationToken ct = default)
    {
        var all = Store.Where(l => l.TenantId == tenantId).ToList();
        IReadOnlyList<ContactList> items = all.Skip((page - 1) * size).Take(size).ToList();
        return Task.FromResult(new PagedResult<ContactList>(items, page, size, all.Count));
    }

    public Task AddAsync(ContactList list, CancellationToken ct = default)
    {
        Store.Add(list);
        return Task.CompletedTask;
    }
}
