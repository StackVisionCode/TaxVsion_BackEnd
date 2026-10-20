using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Stub de IRecipientResolver que devuelve "sin destinatario" para toda audiencia. Util para los
/// consumers que ahora inyectan el resolver (T7.4) pero sus tests no quieren ejercitar la rama
/// nueva (p.ej. SignerInvited sin MappedCustomerId).
/// </summary>
internal sealed class NoPortalRecipientResolver : IRecipientResolver
{
    public static readonly NoPortalRecipientResolver Instance = new();

    public Task<IReadOnlyList<Guid>> ResolveAsync(ByPermission audience, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);

    public Task<Result<IReadOnlyList<Guid>>> ResolveAsync(
        NotificationAudience audience,
        RecipientValidation validation,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Result.Failure<IReadOnlyList<Guid>>(
                new Error("Notification.Audience.Empty", "Stub resolver returns no recipients.")
            )
        );
}
