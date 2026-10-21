namespace TaxVision.Correspondence.Api.Requests;

/// <summary><c>POST /correspondence/drafts</c> (Fase 11).</summary>
/// <param name="CustomerId">Cliente en cuya ficha se archiva el correo. Null = correo a alguien que no es cliente.</param>
public sealed record CreateDraftBody(Guid? CustomerId, Guid AccountId);
