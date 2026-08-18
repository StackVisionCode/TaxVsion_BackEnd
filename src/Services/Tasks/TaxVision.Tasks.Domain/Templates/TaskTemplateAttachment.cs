namespace TaxVision.Tasks.Domain.Templates;

/// <summary>
/// Un archivo de referencia del guion: el checklist en PDF, el formulario en blanco, la carta modelo.
/// Todas las instancias comparten este mismo <see cref="FileId"/> —el byte se guarda una vez en
/// CloudStorage y se referencia N veces—.
/// </summary>
/// <remarks>
/// El <see cref="Id"/> lo genera el dominio, así que su config EF necesita <c>ValueGeneratedNever()</c>.
/// </remarks>
public sealed class TaskTemplateAttachment
{
    public Guid Id { get; }
    public Guid TemplateId { get; private set; }

    public Guid FileId { get; }
    public string DisplayName { get; } = string.Empty;
    public string? ContentType { get; }
    public long SizeBytes { get; }

    /// <summary>
    /// A qué paso se engancha. Sin paso, va a la primera tarea: el checklist del encargo cuelga de
    /// donde el preparador empieza a mirar.
    /// </summary>
    public int? StepOrder { get; }

    private TaskTemplateAttachment() { }

    private TaskTemplateAttachment(Guid fileId, string displayName, string? contentType, long sizeBytes, int? stepOrder)
    {
        Id = Guid.NewGuid();
        FileId = fileId;
        DisplayName = displayName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        StepOrder = stepOrder;
    }

    internal static TaskTemplateAttachment Create(
        Guid fileId,
        string displayName,
        string? contentType,
        long sizeBytes,
        int? stepOrder
    ) => new(fileId, displayName, contentType, sizeBytes, stepOrder);

    internal void AttachTo(Guid templateId) => TemplateId = templateId;
}
