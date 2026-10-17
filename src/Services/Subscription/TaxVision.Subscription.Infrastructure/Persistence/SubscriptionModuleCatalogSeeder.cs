using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Infrastructure.Configuration;

namespace TaxVision.Subscription.Infrastructure.Persistence;

public sealed record ModuleSeedDefinition(
    Guid Id,
    string Code,
    string Name,
    string Description,
    ModuleCategory Category,
    decimal? MonthlyUsd = null,
    decimal? YearlyUsd = null
);

public static class SubscriptionModuleCatalogSeeder
{
    public static readonly IReadOnlyList<ModuleSeedDefinition> Definitions =
    [
        Core(
            "b2000000-0000-0000-0000-000000000001",
            "customers",
            "Customers",
            "Customer directory, imports and assignments."
        ),
        Core(
            "b2000000-0000-0000-0000-000000000002",
            "signatures",
            "Signatures",
            "Electronic signature requests and audit."
        ),
        Core(
            "b2000000-0000-0000-0000-000000000003",
            "documents",
            "Documents",
            "Storage, templates and shared documents."
        ),
        Core("b2000000-0000-0000-0000-000000000004", "planner", "Planner", "Calendar, reminders, tasks and notes."),
        Commercial(
            "b2000000-0000-0000-0000-000000000005",
            "email",
            "Email",
            "Correspondence inbox, connectors and postmaster.",
            29m,
            290m
        ),
        Commercial(
            "b2000000-0000-0000-0000-000000000006",
            "meetings",
            "Meetings",
            "Scheduled meetings for office staff and customers.",
            29m,
            290m
        ),
        Core("b2000000-0000-0000-0000-000000000007", "comms", "Communication", "Chat, voice and video communication."),
        Commercial(
            "b2000000-0000-0000-0000-000000000008",
            "campaigns",
            "Campaigns",
            "Outbound email campaigns and senders.",
            29m,
            290m
        ),
        Core(
            "b2000000-0000-0000-0000-000000000009",
            "wallet",
            "Wallet",
            "Tenant wallet balance, top-ups and spend audit."
        ),
    ];

    public static async Task SeedAsync(
        SubscriptionDbContext db,
        SubscriptionCatalogOptions options,
        ILogger logger,
        CancellationToken ct
    )
    {
        var existingCount = await db.Modules.CountAsync(ct);
        if (existingCount > 0 && options.ModuleCatalog.SeedOnlyIfEmpty)
        {
            logger.LogInformation(
                "Module catalog already present ({ModuleCount} modules); seeder is passive.",
                existingCount
            );
            return;
        }

        foreach (var definition in Definitions)
        {
            var module = await db.Modules.FirstOrDefaultAsync(candidate => candidate.Code == definition.Code, ct);
            if (module is null)
            {
                module = Module.Seed(definition.Id, definition.Code, definition.Name, definition.Description).Value;
                db.Modules.Add(module);
            }

            if (definition.Category == ModuleCategory.Core)
            {
                module.MarkAsCore();
                continue;
            }

            module.MarkAsCommercial();
            if (definition.MonthlyUsd is not null && definition.YearlyUsd is not null)
                module.SetAsAddon(definition.MonthlyUsd.Value, definition.YearlyUsd.Value);
            else
                module.RemoveAddonPricing();
        }

        await db.SaveChangesAsync(ct);
    }

    private static ModuleSeedDefinition Core(string id, string code, string name, string description) =>
        new(new Guid(id), code, name, description, ModuleCategory.Core);

    private static ModuleSeedDefinition Commercial(
        string id,
        string code,
        string name,
        string description,
        decimal? monthlyUsd = null,
        decimal? yearlyUsd = null
    ) => new(new Guid(id), code, name, description, ModuleCategory.Commercial, monthlyUsd, yearlyUsd);
}
