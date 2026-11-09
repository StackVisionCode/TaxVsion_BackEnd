using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Tenant.Application.Profiles.Abstractions;
using TaxVision.Tenant.Domain.Profiles;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Tenant.Application.Profiles.Commands;

/// <summary>PlatformAdmin: copia una vez los emisores de Billing a las oficinas que aún no tienen perfil.</summary>
public sealed record ImportOfficeProfilesFromBillingCommand(Guid ActorId);

/// <summary>Una página ya leída de Billing: se confirma sola, así un choque no se lleva las anteriores.</summary>
public sealed record ImportOfficeProfilesPageCommand(Guid ActorId, IReadOnlyList<BillingIssuerProfile> Items);

/// <summary>Skipped = suma de los tres motivos: no es una oficina, ya tenía perfil, nada utilizable.</summary>
public sealed record ImportOfficeProfilesResponse(
    int Imported,
    int Skipped,
    int SkippedNotAnOffice,
    int SkippedExistingProfile,
    int SkippedNothingUsable
)
{
    public static readonly ImportOfficeProfilesResponse Empty = new(0, 0, 0, 0, 0);

    public ImportOfficeProfilesResponse Add(ImportPageOutcome page) =>
        new(
            Imported + page.Imported.Count,
            Skipped + page.NotAnOffice + page.ExistingProfile + page.NothingUsable,
            SkippedNotAnOffice + page.NotAnOffice,
            SkippedExistingProfile + page.ExistingProfile,
            SkippedNothingUsable + page.NothingUsable
        );
}

public sealed record ImportPageOutcome(
    List<OfficeProfile> Imported,
    int NotAnOffice,
    int ExistingProfile,
    int NothingUsable
);

/// <summary>
/// Recorre el listado interno de Billing página a página. Solo llena oficinas sin perfil (nunca pisa uno
/// guardado), así que se puede repetir sin efectos. Los valores viejos mal formados se descartan campo
/// por campo (cada uno queda en el log) y el resto se importa. El HTTP a Billing va fuera de toda transacción;
/// cada página se guarda en <see cref="ImportOfficeProfilesPageHandler"/>. Si una choca, quedan las anteriores y
/// repetir la importación sigue donde iba.
/// </summary>
public static class ImportOfficeProfilesFromBillingHandler
{
    public const int PageSize = 100;

    [NonTransactional]
    public static async Task<Result<ImportOfficeProfilesResponse>> Handle(
        ImportOfficeProfilesFromBillingCommand command,
        IBillingIssuerProfileClient billing,
        IMessageBus bus,
        ILogger<ImportOfficeProfilesFromBillingCommand> logger,
        CancellationToken ct
    )
    {
        var totals = ImportOfficeProfilesResponse.Empty;
        for (var page = 1; ; page++)
        {
            var fetched = await billing.GetPageAsync(page, PageSize, ct);
            if (fetched.IsFailure)
            {
                logger.LogWarning(
                    "Billing issuer profile import stopped at page {Page} ({Imported} imported, {Skipped} skipped so far).",
                    page,
                    totals.Imported,
                    totals.Skipped
                );
                return Result.Failure<ImportOfficeProfilesResponse>(fetched.Error);
            }

            var outcome = await bus.InvokeAsync<ImportPageOutcome>(
                new ImportOfficeProfilesPageCommand(command.ActorId, fetched.Value.Items),
                ct
            );
            totals = totals.Add(outcome);

            if (IsLastPage(fetched.Value, page))
                break;
        }

        logger.LogInformation(
            "Billing issuer profile import finished by platformAdmin={ActorId}: {Imported} imported, {Skipped} skipped ({NotAnOffice} not an office, {ExistingProfile} already had a profile, {NothingUsable} nothing usable).",
            command.ActorId,
            totals.Imported,
            totals.Skipped,
            totals.SkippedNotAnOffice,
            totals.SkippedExistingProfile,
            totals.SkippedNothingUsable
        );
        return Result.Success(totals);
    }

    private static bool IsLastPage(BillingIssuerProfilePage page, int pageNumber) =>
        page.Items.Count == 0 || (long)pageNumber * PageSize >= page.TotalCount;
}

/// <summary>Guarda la página y recién después publica <c>TenantProfileChanged</c> por cada perfil importado.</summary>
public static class ImportOfficeProfilesPageHandler
{
    // Marcador que estampaba Billing en un emisor sin calle antes de F0; no es un dato.
    private const string LegacyPlaceholder = "—";

    public static async Task<ImportPageOutcome> Handle(
        ImportOfficeProfilesPageCommand command,
        IOfficeProfileRepository profiles,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<ImportOfficeProfilesPageCommand> logger,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var outcome = await ImportPage(
            command.Items,
            command.ActorId,
            clock.GetUtcNow().UtcDateTime,
            profiles,
            logger,
            ct
        );
        await PersistAndPublish(outcome.Imported, unitOfWork, bus, correlation, ct);
        return outcome;
    }

    private static async Task<ImportPageOutcome> ImportPage(
        IReadOnlyList<BillingIssuerProfile> items,
        Guid actorId,
        DateTime nowUtc,
        IOfficeProfileRepository profiles,
        ILogger logger,
        CancellationToken ct
    )
    {
        var candidates = items.Select(item => item.TenantId).Where(id => id != Guid.Empty).Distinct().ToList();
        var known =
            candidates.Count == 0
                ? ImportCandidates.None
                : await profiles.ClassifyImportCandidatesAsync(candidates, ct);
        var handled = new HashSet<Guid>();

        var imported = new List<OfficeProfile>();
        int notAnOffice = 0,
            existingProfile = 0,
            nothingUsable = 0;
        foreach (var item in items)
        {
            // Filas de Billing de oficinas que ya no existen (o de pruebas) no se importan.
            if (!known.Offices.Contains(item.TenantId))
            {
                notAnOffice++;
                continue;
            }

            // Ya tenía perfil, o el mismo tenant vino repetido en la página: nunca se pisa.
            if (known.WithProfile.Contains(item.TenantId) || !handled.Add(item.TenantId))
            {
                existingProfile++;
                continue;
            }

            var result = OfficeProfile.ImportFromLegacy(item.TenantId, ToDraft(item), actorId, nowUtc);
            foreach (var dropped in result.DroppedFields)
                logger.LogWarning(
                    "Billing issuer profile of tenant {TenantId}: dropped {Field} ({Reason}).",
                    item.TenantId,
                    dropped.Code,
                    dropped.Message
                );

            if (result.Profile is null)
            {
                nothingUsable++;
                continue;
            }

            await profiles.AddAsync(result.Profile, ct);
            imported.Add(result.Profile);
        }
        return new ImportPageOutcome(imported, notAnOffice, existingProfile, nothingUsable);
    }

    private static async Task PersistAndPublish(
        IReadOnlyList<OfficeProfile> imported,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (imported.Count == 0)
            return;

        await unitOfWork.SaveChangesAsync(ct);
        foreach (var profile in imported)
            await bus.PublishAsync(OfficeProfileChangedEvents.From(profile, correlation.CorrelationId));
    }

    private static OfficeProfileDraft ToDraft(BillingIssuerProfile item) =>
        new(
            LegalName: item.Name,
            DisplayName: null,
            EntityType: null,
            Ein: item.TaxId,
            Phone: item.Phone,
            Email: item.Email,
            Website: item.Website,
            PhysicalAddress: new OfficeAddressDraft(
                item.Line1 == LegacyPlaceholder ? null : item.Line1,
                item.Line2,
                item.City,
                item.State,
                item.Zip,
                item.Country
            ),
            MailingSameAsPhysical: true,
            MailingAddress: null,
            DefaultLanguage: null,
            BusinessHours: null,
            BookingUrl: null,
            ReviewUrl: null
        );
}
