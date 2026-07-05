using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Accounts;

namespace TaxVision.Notification.Infrastructure.Providers;

/// <summary>Resuelve el adaptador de proveedor por tipo (IMAP, Gmail API, Microsoft Graph).</summary>
public sealed class EmailProviderAdapterFactory(IEnumerable<IEmailProviderAdapter> adapters) : IEmailProviderAdapterFactory
{
    public IEmailProviderAdapter Resolve(EmailExternalProvider provider) =>
        adapters.FirstOrDefault(a => a.Provider == provider)
        ?? throw new InvalidOperationException($"No adapter registered for provider '{provider}'.");
}
