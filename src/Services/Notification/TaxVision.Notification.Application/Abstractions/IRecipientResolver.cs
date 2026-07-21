namespace TaxVision.Notification.Application.Abstractions;

public interface IRecipientResolver
{
    Task<IReadOnlyList<Guid>> ResolveAsync(ByPermission audience, CancellationToken ct = default);
}
