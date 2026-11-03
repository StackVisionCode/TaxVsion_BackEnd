using BuildingBlocks.Common;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Tenant.Application.Profiles.Abstractions;
using Wolverine;

namespace TaxVision.Tenant.Application.Profiles.Commands;

/// <summary>
/// PlatformAdmin: vuelve a anunciar el perfil guardado de cada oficina para poblar o reparar las
/// proyecciones (Billing, Scribe, Postmaster, Signature, PaymentApp, PaymentClient). Idempotente: cada
/// evento lleva su versión y los consumidores ignoran una igual, así que repetirlo no cambia nada.
/// </summary>
public sealed record RepublishOfficeProfilesCommand(Guid ActorId);

public sealed record RepublishOfficeProfilesResult(int Published);

public static class RepublishOfficeProfilesHandler
{
    public const int PageSize = 200;

    public static async Task<Result<RepublishOfficeProfilesResult>> Handle(
        RepublishOfficeProfilesCommand command,
        IOfficeProfileRepository profiles,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<RepublishOfficeProfilesCommand> logger,
        CancellationToken ct
    )
    {
        var published = 0;
        for (var skip = 0; ; skip += PageSize)
        {
            var page = await profiles.ListSavedAsync(skip, PageSize, ct);
            foreach (var profile in page)
                await bus.PublishAsync(OfficeProfileChangedEvents.From(profile, correlation.CorrelationId));
            published += page.Count;

            if (page.Count < PageSize)
                break;
        }

        logger.LogInformation(
            "Office profiles republished by platformAdmin={ActorId}: {Published} profile event(s).",
            command.ActorId,
            published
        );
        return Result.Success(new RepublishOfficeProfilesResult(published));
    }
}
