namespace BuildingBlocks.Jobs;

public enum JobUnitOutcome
{
    /// <summary>Cambios y avisos confirmados juntos.</summary>
    Committed,

    /// <summary>La acción dijo que no había nada que hacer; no se guardó nada.</summary>
    Skipped,

    /// <summary>Otro proceso tocó la fila (concurrencia). No quedó nada; el barrido siguiente la retoma.</summary>
    Conflict,

    /// <summary>Error en la unidad. No quedó nada y las demás unidades siguen.</summary>
    Failed,
}

public readonly record struct JobUnitResult(JobUnitOutcome Outcome, Exception? Exception = null);

public readonly record struct JobBatchSummary(int Committed, int Skipped, int Conflicts, int Failed)
{
    public int Total => Committed + Skipped + Conflicts + Failed;
}
