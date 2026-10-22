namespace BuildingBlocks.Web.Webhooks;

public sealed record WebhookSecretCheck(WebhookSecretVerdict Verdict, WebhookSecretSource Source);
