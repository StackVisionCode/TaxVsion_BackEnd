using BuildingBlocks.Domain;

namespace TaxVision.Tasks.ClientForms.Domain.Requests;

/// <summary>
/// Fila append-only del índice de reporting (P-12): un valor normalizado de un componente
/// <c>reportable</c> de la revisión completada. Se escribe en la misma transacción de Complete y
/// nunca se muta ni se borra. Al menos uno de <see cref="CustomerId"/> o <see cref="ParticipantId"/>
/// viene poblado (P-14.10).
/// </summary>
public sealed class FormAnswerIndexEntry : TenantEntity
{
    private FormAnswerIndexEntry() { }

    public Guid FormRequestId { get; private set; }
    public Guid FormTemplateId { get; private set; }
    public int VersionNumber { get; private set; }
    public Guid? CustomerId { get; private set; }
    public Guid? ParticipantId { get; private set; }
    public int RevisionNumber { get; private set; }
    public string Key { get; private set; } = string.Empty;

    // 1-based en secciones repeater; null fuera de repeaters.
    public int? RowIndex { get; private set; }
    public string ValueText { get; private set; } = string.Empty;
    public decimal? ValueNumber { get; private set; }
    public DateOnly? ValueDate { get; private set; }
    public DateTime CompletedAtUtc { get; private set; }

    public const int KeyMaxLength = 64;
    public const int ValueTextMaxLength = 400;

    public static FormAnswerIndexEntry Create(
        Guid tenantId,
        Guid formRequestId,
        Guid formTemplateId,
        int versionNumber,
        Guid? customerId,
        Guid? participantId,
        int revisionNumber,
        string key,
        int? rowIndex,
        string valueText,
        decimal? valueNumber,
        DateOnly? valueDate,
        DateTime completedAtUtc
    )
    {
        if (customerId is null && participantId is null)
            throw new ArgumentException("An index entry needs a customer or a participant.");
        var entry = new FormAnswerIndexEntry
        {
            FormRequestId = formRequestId,
            FormTemplateId = formTemplateId,
            VersionNumber = versionNumber,
            CustomerId = customerId,
            ParticipantId = participantId,
            RevisionNumber = revisionNumber,
            Key = Trim(key, KeyMaxLength),
            RowIndex = rowIndex,
            ValueText = Trim(valueText, ValueTextMaxLength),
            ValueNumber = valueNumber,
            ValueDate = valueDate,
            CompletedAtUtc = completedAtUtc,
        };
        entry.SetTenant(tenantId);
        return entry;
    }

    private static string Trim(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}
