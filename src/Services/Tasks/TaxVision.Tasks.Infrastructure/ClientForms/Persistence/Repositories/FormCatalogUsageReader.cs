using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Mapping.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories;

// Recorre todos los tenants: lo consume solo la plataforma antes de retirar un destino.
internal sealed class FormCatalogUsageReader(TasksDbContext context) : IFormCatalogUsageReader
{
    private const string LikeEscape = "\\";

    public async Task<FormCatalogUsageCounts> CountAsync(
        string targetKey,
        DateTime nowUtc,
        CancellationToken ct = default
    )
    {
        // El JSON canónico no lleva espacios, así que la clave aparece siempre con esta forma exacta.
        var pattern = $"%\"targetKey\":\"{EscapeLike(targetKey)}\"%";
        var versionIds = context
            .FormTemplateVersions.IgnoreQueryFilters()
            .Where(version => EF.Functions.Like(version.MappingSetJson, pattern, LikeEscape))
            .Select(version => version.Id);
        var openRequests = await context
            .FormRequests.IgnoreQueryFilters()
            .CountAsync(
                request =>
                    (
                        request.Status == FormRequestStatus.Pending
                        || request.Status == FormRequestStatus.Submitted
                        || request.Status == FormRequestStatus.Returned
                    ) && versionIds.Contains(request.FormTemplateVersionId),
                ct
            );
        var activeLinks = await context
            .FormIntakeLinks.IgnoreQueryFilters()
            .CountAsync(
                link =>
                    link.Status != FormIntakeLinkStatus.Revoked
                    && (link.ExpiresAtUtc == null || link.ExpiresAtUtc > nowUtc)
                    && versionIds.Contains(link.FormTemplateVersionId),
                ct
            );
        return new FormCatalogUsageCounts(openRequests, activeLinks, await versionIds.CountAsync(ct));
    }

    private static string EscapeLike(string value) =>
        value
            .Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal)
            .Replace("[", LikeEscape + "[", StringComparison.Ordinal);
}
