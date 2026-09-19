using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Campaigns.Domain.Contacts;

/// <summary>
/// Aggregate root de una <b>lista de contactos</b> (<c>Domain_Design.md §6</c>). Agrupa contactos por
/// membresía (join <see cref="ContactListMember"/> — un contacto puede estar en varias listas). Una
/// campaña referencia listas por id; la audiencia se resuelve por run (nunca se materializa dentro de
/// la lista ni de la campaña). SIN dinero.
/// </summary>
public sealed class ContactList : TenantEntity
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 1000;

    private readonly List<ContactListMember> _members = [];

    private ContactList() { }

    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<ContactListMember> Members => _members.AsReadOnly();

    public static Result<ContactList> Create(Guid tenantId, string name, string? description)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<ContactList>(ContactListErrors.TenantRequired);

        var norm = Normalize(name, description);
        if (norm.IsFailure)
            return Result.Failure<ContactList>(norm.Error);

        var now = DateTime.UtcNow;
        var list = new ContactList
        {
            Id = Guid.NewGuid(),
            Name = norm.Value.Name,
            Description = norm.Value.Description,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        list.SetTenant(tenantId);
        return Result.Success(list);
    }

    public Result Update(string name, string? description)
    {
        var norm = Normalize(name, description);
        if (norm.IsFailure)
            return Result.Failure(norm.Error);

        Name = norm.Value.Name;
        Description = norm.Value.Description;
        Touch();
        return Result.Success();
    }

    /// <summary>Agrega un contacto a la lista (idempotente — no duplica membresías).</summary>
    public Result AddMember(Guid contactId)
    {
        if (contactId == Guid.Empty)
            return Result.Failure(ContactErrors.NotFound);
        if (_members.Any(m => m.ContactId == contactId))
            return Result.Success(); // ya es miembro

        _members.Add(new ContactListMember(Id, TenantId, contactId));
        Touch();
        return Result.Success();
    }

    public Result RemoveMember(Guid contactId)
    {
        var member = _members.Find(m => m.ContactId == contactId);
        if (member is null)
            return Result.Failure(ContactListErrors.MemberNotFound);

        _members.Remove(member);
        Touch();
        return Result.Success();
    }

    private static Result<(string Name, string? Description)> Normalize(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<(string, string?)>(ContactListErrors.NameRequired);

        var n = name.Trim();
        if (n.Length > MaxNameLength)
            return Result.Failure<(string, string?)>(ContactListErrors.NameTooLong);

        var d = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (d is { Length: > MaxDescriptionLength })
            return Result.Failure<(string, string?)>(ContactListErrors.DescriptionTooLong);

        return Result.Success((n, d));
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}

/// <summary>
/// Membresía (join) de un <see cref="Contact"/> en una <see cref="ContactList"/>. Entidad hija —
/// nunca se crea/muta fuera del aggregate. Lleva <c>TenantId</c> como columna (para consultas), pero
/// NO es <c>ITenantOwned</c>: se carga vía el padre (que sí filtra por tenant), igual que
/// <c>CampaignRecipient</c>.
/// </summary>
public sealed class ContactListMember
{
    public Guid Id { get; }
    public Guid ContactListId { get; }
    public Guid TenantId { get; }
    public Guid ContactId { get; }
    public DateTime AddedAtUtc { get; }

    private ContactListMember() { }

    internal ContactListMember(Guid contactListId, Guid tenantId, Guid contactId)
    {
        Id = Guid.NewGuid();
        ContactListId = contactListId;
        TenantId = tenantId;
        ContactId = contactId;
        AddedAtUtc = DateTime.UtcNow;
    }
}
