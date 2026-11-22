using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>
/// <see cref="IRecipientLocale"/> que da el mismo idioma (y zona) a todos y cuenta las lecturas por usuario.
/// <see cref="ByUser"/> fija otro idioma para usuarios concretos.
/// </summary>
internal sealed class FixedRecipientLocale(string language, string? timeZoneId = null) : IRecipientLocale
{
    public static FixedRecipientLocale English => new("en");

    public List<Guid> UserLookups { get; } = [];

    public List<string> OfficeEmailLookups { get; } = [];

    public Dictionary<Guid, string> ByUser { get; } = [];

    public Task<RecipientLocale> ForUserAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        UserLookups.Add(userId);
        return Task.FromResult(new RecipientLocale(ByUser.GetValueOrDefault(userId, language), timeZoneId, false));
    }

    public Task<RecipientLocale> ForCustomerAsync(Guid tenantId, Guid customerId, CancellationToken ct) =>
        Task.FromResult(new RecipientLocale(language, timeZoneId, false));

    public Task<RecipientLocale> ForOfficeEmailAsync(Guid tenantId, string email, CancellationToken ct)
    {
        OfficeEmailLookups.Add(email);
        return Task.FromResult(new RecipientLocale(language, timeZoneId, false));
    }

    public RecipientLocale FromEventLanguage(string? eventLanguage) => new(language, timeZoneId, false);
}
