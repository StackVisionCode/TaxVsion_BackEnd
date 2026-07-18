namespace TaxVision.Correspondence.Infrastructure.Jobs;

/// <summary>Fase 16, plan §30 — auto-descarte de <c>Draft</c>s abandonados (nunca enviados ni descartados por el usuario).</summary>
public sealed class DraftCleanupOptions
{
    public const string SectionName = "Correspondence:DraftCleanup";

    /// <summary>Deshabilitado por default — mismo criterio que <c>ConnectorsRetentionOptions</c>/<c>ScribeRetentionOptions</c>: requiere autorización explícita antes de mutar autónomamente en producción.</summary>
    public bool Enabled { get; set; }

    public int AbandonedAfterDays { get; set; } = 30;

    public int BatchSize { get; set; } = 200;
}
