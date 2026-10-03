using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Application.Layouts;
using TaxVision.Scribe.Application.Rendering;
using TaxVision.Scribe.Application.Templates;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Application.Templates.Commands.SetDispatchScope;

/// <summary><paramref name="Scope"/> null suelta el override y devuelve la decision al layout.</summary>
public sealed record SetTemplateDispatchScopeCommand(string TemplateKey, string? Scope, Guid ActorUserId);

public sealed record SetTemplateDispatchScopeResult(string TemplateKey, string EffectiveScope, bool IsOverridden);

/// <summary>
/// Fija a mano el carril de una plantilla. Solo PlatformAdmin: el carril decide desde que buzon sale el
/// correo, y eso vale para todas las oficinas a la vez.
///
/// <para>La validacion que importa esta abajo: no se puede poner <c>System</c> en una plantilla que vive
/// sobre <c>tenant-base</c>. Ese correo lleva la cascara y el logo de la oficina; mandarlo desde el
/// buzon de la plataforma produce un mensaje que se presenta como de la oficina y sale de otro lado —
/// que es justo lo que los filtros de spam castigan.</para>
/// </summary>
public static class SetTemplateDispatchScopeHandler
{
    private const string TenantLayoutKey = "tenant-base";

    public static async Task<Result<SetTemplateDispatchScopeResult>> Handle(
        SetTemplateDispatchScopeCommand command,
        IEmailTemplateRepository templates,
        IEmailLayoutRepository layouts,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var keyResult = TemplateKey.Create(command.TemplateKey);
        if (keyResult.IsFailure)
            return Result.Failure<SetTemplateDispatchScopeResult>(keyResult.Error);

        // Solo plantillas de plataforma (TenantId null): un override por oficina no existe, el carril
        // es de la plantilla.
        var templateResult = await templates.GetByKeyAsync(keyResult.Value, tenantId: null, ct);
        if (templateResult.IsFailure)
            return Result.Failure<SetTemplateDispatchScopeResult>(templateResult.Error);

        var template = templateResult.Value;
        var layoutKey = await ResolvePublishedLayoutKeyAsync(template, layouts, ct);

        if (command.Scope == DispatchScopes.System && layoutKey == TenantLayoutKey)
            return Result.Failure<SetTemplateDispatchScopeResult>(
                new Error(
                    "EmailTemplate.LaneContradictsLayout",
                    "This template renders on the office shell, so it cannot be sent from the platform mailbox."
                )
            );

        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var applied = template.OverrideDispatchScope(command.Scope, command.ActorUserId, nowUtc);
        if (applied.IsFailure)
            return Result.Failure<SetTemplateDispatchScopeResult>(applied.Error);

        await unitOfWork.SaveChangesAsync(ct);

        var effective =
            command.Scope ?? (layoutKey == TenantLayoutKey ? DispatchScopes.TenantPreferred : DispatchScopes.System);
        return Result.Success(
            new SetTemplateDispatchScopeResult(keyResult.Value.Value, effective, command.Scope is not null)
        );
    }

    /// <summary>
    /// El layout de la version publicada. Sin version publicada no hay layout que contradecir, asi que
    /// la validacion no aplica y el override entra igual.
    /// </summary>
    private static async Task<string?> ResolvePublishedLayoutKeyAsync(
        Domain.Templates.EmailTemplate template,
        IEmailLayoutRepository layouts,
        CancellationToken ct
    )
    {
        var published = template.Versions.FirstOrDefault(v => v.Status == Domain.EmailVersionStatus.Published);
        if (published is null)
            return null;

        var layout = await layouts.GetByIdAsync(published.LayoutId, ct);
        return layout.IsSuccess ? layout.Value.LayoutKey.Value : null;
    }
}
