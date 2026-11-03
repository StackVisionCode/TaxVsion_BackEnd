using TaxVision.Correspondence.Application.Search;

namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Lectura de la búsqueda unificada (<c>/correspondence/search</c>): mismas reglas de visibilidad que el
/// Inbox y Sent/Drafts/Trash. "Contiene" con LIKE acotado por tenant y por <see cref="MailSearchCriteria.Limit"/>
/// (sin texto completo, D-6). Más reciente primero.
/// </summary>
public interface IMailSearchRepository
{
    Task<IReadOnlyList<EmailSearchHit>> SearchEmailsAsync(MailSearchCriteria criteria, CancellationToken ct = default);

    Task<IReadOnlyList<FileSearchHit>> SearchFilesAsync(MailSearchCriteria criteria, CancellationToken ct = default);

    /// <summary>Corresponsales distintos por dirección, agregados en SQL, por último contacto.</summary>
    Task<IReadOnlyList<PersonSearchHit>> SearchPeopleAsync(MailSearchCriteria criteria, CancellationToken ct = default);
}
