using BuildingBlocks.Domain;

namespace TaxVision.Tasks.ClientForms.Domain.Requests;

public sealed class FormSensitiveAnswer : BaseEntity
{
    private FormSensitiveAnswer() { }

    public Guid FormSubmissionId { get; private set; }
    public string ComponentId { get; private set; } = string.Empty;
    public string? RowId { get; private set; }
    public FormTaxIdentifierKind Kind { get; private set; }
    public ProtectedTaxIdentifier Protected { get; private set; } = default!;
    public DateTime CapturedAtUtc { get; private set; }
    public FormActor CapturedBy { get; private set; } = default!;
    public DateTime? SupersededAtUtc { get; private set; }
    public FormAnswerAddress Address => new(ComponentId, RowId);

    internal static FormSensitiveAnswer Create(
        Guid submissionId,
        FormAnswerAddress address,
        FormTaxIdentifierKind kind,
        ProtectedTaxIdentifier value,
        FormActor actor,
        DateTime nowUtc
    ) =>
        new()
        {
            FormSubmissionId = submissionId,
            ComponentId = address.ComponentId,
            RowId = address.RowId,
            Kind = kind,
            Protected = value,
            CapturedAtUtc = nowUtc,
            CapturedBy = actor.Copy(),
        };

    internal void Supersede(DateTime nowUtc) => SupersededAtUtc ??= nowUtc;

    // Re-cifra el identificador con la clave actual (X-9). El Last4 no cambia: el texto es el mismo.
    public void ReEncrypt(ProtectedTaxIdentifier reEncrypted)
    {
        ArgumentNullException.ThrowIfNull(reEncrypted);
        Protected = reEncrypted;
    }
}
