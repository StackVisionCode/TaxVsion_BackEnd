using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Domain.Plans;

namespace TaxVision.Subscription.Application.Catalog.Revisions;

/// <summary>
/// R3 (2026-11-11): las descripciones sembradas estaban en español y nombraban cifras que ya no valen ("25 usuarios").
/// Se reemplazan por textos en inglés sin cifras: usuarios, almacenamiento y precio los muestra la edición. Solo se
/// cambia una descripción que todavía tiene un texto sembrado viejo; una editada por el admin de plataforma no se toca.
/// Idempotente.
/// </summary>
public sealed class R3PlanDescriptionsRevision : ICatalogRevision
{
    private static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        ["starter"] = "For offices getting started: clients, e-signatures and documents in one place.",
        ["pro"] = "For growing offices: adds integrated email, video meetings and marketing campaigns.",
        ["enterprise"] =
            "Everything your office needs: every module, from client files to email, meetings and campaigns.",
    };

    // Textos que sembraron versiones anteriores; solo esos se reemplazan.
    private static readonly HashSet<string> SeededBefore = new(StringComparer.Ordinal)
    {
        "Para oficinas que estan empezando: 3 usuarios, clientes, firmas y documentos.",
        "Para oficinas en crecimiento: 10 usuarios, correo, comunicacion y campanas.",
        "Para multiservices con equipos grandes: 25 usuarios y todos los modulos.",
        "Para multiservices con equipos grandes: 25 usuarios, 200 GB y todos los modulos.",
    };

    public string Id => "2026-11-11-r3-plan-descriptions";

    public string Description => "Replace the seeded plan descriptions (Spanish, outdated figures) with English ones.";

    /// <summary>La descripción vigente de un plan del catálogo inicial; la misma que siembra una base nueva.</summary>
    public static string DescriptionFor(string planCode) => Descriptions[planCode];

    public async Task<Result<string>> ApplyAsync(CatalogRevisionContext context, CancellationToken ct)
    {
        var changes = new List<string>();
        foreach (var listed in await context.Plans.GetAllAsync(ct))
        {
            if (!Descriptions.TryGetValue(listed.Code.Value, out var description))
                continue;

            var plan = await context.Plans.GetByIdForUpdateAsync(listed.Id, ct);
            if (plan is null || plan.Description == description)
                continue;

            if (!SeededBefore.Contains(plan.Description))
            {
                changes.Add($"{plan.Code.Value}: kept (edited)");
                continue;
            }

            var before = CatalogAuditViews.Of(plan);
            var updated = plan.UpdateMetadata(
                plan.Name,
                description,
                CatalogRevisionContext.DeployActor,
                context.NowUtc
            );
            if (updated.IsFailure)
                return Result.Failure<string>(updated.Error);

            await CatalogAudit.RecordIfChangedAsync(
                context.CatalogAudit,
                context.Actor,
                "catalog.revision",
                CatalogAuditViews.Target(plan),
                before,
                CatalogAuditViews.Of(plan),
                reason: context.CorrelationId,
                context.CorrelationId,
                context.NowUtc,
                ct
            );
            changes.Add($"{plan.Code.Value}: description");
        }

        return Result.Success(changes.Count == 0 ? "unchanged" : string.Join("; ", changes));
    }
}
