namespace TaxVision.Correspondence.Api.Requests;

/// <summary><c>POST /correspondence/messages/{id}/reply/draft</c> (Fase 10/11, en <c>MessagesController</c>) — <c>AccountId</c> es la cuenta conectada de Connectors desde la que se responderá, el mensaje original no la determina por sí solo.</summary>
public sealed record StartReplyBody(Guid AccountId);
