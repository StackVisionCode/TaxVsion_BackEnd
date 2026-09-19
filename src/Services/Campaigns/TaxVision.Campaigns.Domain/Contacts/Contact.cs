using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Campaigns.Domain.Campaigns;

namespace TaxVision.Campaigns.Domain.Contacts;

/// <summary>
/// Aggregate root de un <b>contacto</b> — la libreta propia de Campaigns (puede NO ser cliente),
/// importable y con opt-out/consentimiento por canal (<c>Domain_Design.md §6</c>). Larga vida y
/// editable fuera de una campaña; la campaña solo lo referencia por id. SIN dinero.
/// El destino se guarda normalizado (email en minúsculas, teléfono trim) para dedupe estable.
/// </summary>
public sealed class Contact : TenantEntity
{
    public const int MaxNameLength = 200;
    public const int MaxEmailLength = 320;
    public const int MaxPhoneLength = 32;

    private Contact() { }

    public string? Name { get; private set; }

    /// <summary>Email normalizado (trim + minúsculas). Null si el contacto no tiene email.</summary>
    public string? Email { get; private set; }

    /// <summary>Teléfono E.164 (trim). Null si el contacto no tiene teléfono.</summary>
    public string? PhoneE164 { get; private set; }

    public ContactSource Source { get; private set; }

    /// <summary>Id del cliente en Customer si además es cliente (source <see cref="ContactSource.FromCustomer"/>); si no, null.</summary>
    public Guid? CustomerRef { get; private set; }

    /// <summary>Canales de los que el contacto se dio de baja (consentimiento). Un canal opt-out se excluye/omite al materializar la audiencia.</summary>
    public CampaignChannel OptedOutChannels { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<Contact> Create(
        Guid tenantId,
        string? name,
        string? email,
        string? phoneE164,
        ContactSource source,
        Guid? customerRef = null
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<Contact>(ContactErrors.TenantRequired);

        var norm = Normalize(name, email, phoneE164);
        if (norm.IsFailure)
            return Result.Failure<Contact>(norm.Error);

        var now = DateTime.UtcNow;
        var contact = new Contact
        {
            Id = Guid.NewGuid(),
            Name = norm.Value.Name,
            Email = norm.Value.Email,
            PhoneE164 = norm.Value.Phone,
            Source = source,
            CustomerRef = customerRef,
            OptedOutChannels = CampaignChannel.None,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        contact.SetTenant(tenantId);
        return Result.Success(contact);
    }

    public Result Update(string? name, string? email, string? phoneE164)
    {
        var norm = Normalize(name, email, phoneE164);
        if (norm.IsFailure)
            return Result.Failure(norm.Error);

        Name = norm.Value.Name;
        Email = norm.Value.Email;
        PhoneE164 = norm.Value.Phone;
        Touch();
        return Result.Success();
    }

    /// <summary>Marca el/los canal(es) como opt-out (idempotente).</summary>
    public void OptOut(CampaignChannel channels)
    {
        OptedOutChannels |= channels;
        Touch();
    }

    /// <summary>Re-suscribe el/los canal(es) (idempotente).</summary>
    public void OptIn(CampaignChannel channels)
    {
        OptedOutChannels &= ~channels;
        Touch();
    }

    public bool IsOptedOut(CampaignChannel channel) => OptedOutChannels.HasFlag(channel);

    /// <summary>Destino resuelto para un canal, o null si el contacto no tiene destino para ese canal.</summary>
    public string? DestinationFor(CampaignChannel channel) =>
        channel switch
        {
            CampaignChannel.Email => Email,
            CampaignChannel.Sms or CampaignChannel.WhatsApp => PhoneE164,
            _ => null,
        };

    private static Result<(string? Name, string? Email, string? Phone)> Normalize(
        string? name,
        string? email,
        string? phone
    )
    {
        var n = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var e = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

        // El teléfono, si viene, debe ser E.164 (con código de país) — se normaliza o se rechaza.
        string? p = null;
        if (!string.IsNullOrWhiteSpace(phone))
        {
            p = PhoneNumbers.ToE164(phone);
            if (p is null)
                return Result.Failure<(string?, string?, string?)>(ContactErrors.PhoneNotE164);
        }

        if (e is null && p is null)
            return Result.Failure<(string?, string?, string?)>(ContactErrors.DestinationRequired);
        if (n is { Length: > MaxNameLength })
            return Result.Failure<(string?, string?, string?)>(ContactErrors.NameTooLong);
        if (e is { Length: > MaxEmailLength })
            return Result.Failure<(string?, string?, string?)>(ContactErrors.EmailTooLong);

        return Result.Success((n, e, p));
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
