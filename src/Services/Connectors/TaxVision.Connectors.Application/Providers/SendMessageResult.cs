namespace TaxVision.Connectors.Application.Providers;

/// <summary>
/// <see cref="ProviderMessageId"/>/<see cref="ProviderThreadId"/> son null para Graph — <c>sendMail</c>
/// y el <c>reply</c> de un paso (v1 de D3, §6.2/§11) devuelven <c>202</c> sin cuerpo, Graph no expone
/// el id nativo del mensaje recién enviado en ese camino (a diferencia de Gmail, que siempre lo
/// devuelve). <c>SentMessage.MarkAsSent</c> en Postmaster ya acepta <c>providerMessageId</c> nullable
/// por esta misma razón.
/// </summary>
public sealed record SendMessageResult(string? ProviderMessageId, string? ProviderThreadId, DateTime SentAtUtc);
