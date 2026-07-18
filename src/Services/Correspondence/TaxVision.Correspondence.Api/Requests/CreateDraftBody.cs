namespace TaxVision.Correspondence.Api.Requests;

/// <summary><c>POST /correspondence/drafts</c> (Fase 11).</summary>
public sealed record CreateDraftBody(Guid CustomerId, Guid AccountId);
