namespace TaxVision.Tasks.Infrastructure.Jobs;

public sealed class TaskRetentionOptions
{
    public const string SectionName = "Tasks:Retention";

    public int IntervalHours { get; set; } = 24;

    /// <summary>
    /// Cuánto sobrevive una referencia ya desadjuntada. Doce meses cubre una temporada fiscal
    /// completa, que es el horizonte en que alguien todavía pregunta «¿qué documento tenía esto?».
    /// </summary>
    public int DetachedAttachmentMonths { get; set; } = 12;

    public int BatchSize { get; set; } = 5000;
}
