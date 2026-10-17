namespace TaxVision.Subscription.Application.Plans.Commands.PublishDraftVersion;

/// <summary>CAT-F2.5 — publica una versión Draft (supersede la publicada anterior si existe) y
/// encola el recálculo masivo para propagar los nuevos entitlements/módulos a los tenants del plan.</summary>
public sealed record PublishDraftVersionCommand(Guid PlanId, Guid VersionId, Guid ActorUserId);
