using TaxVision.Correspondence.Domain.Compose;

namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// Shape pensado para el endpoint de la Fase 11 (<c>POST /correspondence/messages/{id}/reply/draft</c>,
/// plan §22), que devuelve <c>{ draftId, subject, replyContext }</c> para pre-poblar el composer
/// en el frontend — esta fase solo arma el handler, el controller llega en la Fase 11.
/// </summary>
public sealed record StartReplyResult(Guid DraftId, string Subject, ReplyContext ReplyContext);
