namespace TaxVision.Subscription.Application.Plans.Commands.CreateDraftVersion;

/// <summary>CAT-F2.5 — abre una versión Draft nueva (versionNumber = max + 1) para un plan que ya
/// tiene una versión publicada. El autor edita su contenido con <c>PUT .../versions/{v}/draft</c>
/// (F2.4) y luego la publica con <c>POST .../versions/{v}/publish</c>.</summary>
public sealed record CreateDraftVersionCommand(
    Guid PlanId,
    int TrialDaysDefault,
    IReadOnlyList<string> SupportedBillingCycles,
    Guid ActorUserId
);

public sealed record CreateDraftVersionResult(Guid VersionId, int VersionNumber);
