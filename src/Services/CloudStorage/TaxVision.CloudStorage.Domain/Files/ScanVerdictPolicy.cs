namespace TaxVision.CloudStorage.Domain.Files;

public enum ScanModeration
{
    Clean,
    PolicyViolation,
    Uncertain,
}

/// <summary>Lo que encontraron las pasadas del escaneo. Las que no llegaron a correr quedan con su valor por defecto.</summary>
public sealed record ScanFindings(
    bool ContentAllowed,
    bool Infected = false,
    ScanModeration Moderation = ScanModeration.Clean
);

public enum ScanOutcome
{
    ContentRejected,
    Infected,
    BlockedByPolicy,
    PendingReview,
    Clean,
}

/// <summary>A dónde va el objeto: se descarta, a cuarentena o al bucket principal.</summary>
public enum ScanTarget
{
    Discard,
    Quarantine,
    Main,
}

public enum ScanQuotaEffect
{
    Release,
    Commit,
}

/// <summary>Estado, destino del objeto, efecto en la cuota y si se retiene como evidencia.</summary>
public sealed record ScanDecision(
    ScanOutcome Outcome,
    ScanTarget Target,
    ScanQuotaEffect Quota,
    bool RetainAsEvidence,
    string AuditOutcome
);

/// <summary>Servicio de dominio del escaneo: de los hallazgos sale el destino del archivo. Sin I/O.</summary>
public static class ScanVerdictPolicy
{
    public static ScanDecision Decide(ScanFindings findings)
    {
        if (!findings.ContentAllowed)
            return new(ScanOutcome.ContentRejected, ScanTarget.Discard, ScanQuotaEffect.Release, false, "rejected");
        if (findings.Infected)
            return new(ScanOutcome.Infected, ScanTarget.Quarantine, ScanQuotaEffect.Release, false, "infected");

        return findings.Moderation switch
        {
            // Se conserva como evidencia (T-20 §6): el purgador de la papelera salta lo retenido.
            ScanModeration.PolicyViolation => new(
                ScanOutcome.BlockedByPolicy,
                ScanTarget.Quarantine,
                ScanQuotaEffect.Release,
                true,
                "blocked-by-policy"
            ),
            ScanModeration.Uncertain => new(
                ScanOutcome.PendingReview,
                ScanTarget.Quarantine,
                ScanQuotaEffect.Release,
                false,
                "pending-review"
            ),
            _ => new(ScanOutcome.Clean, ScanTarget.Main, ScanQuotaEffect.Commit, false, "clean"),
        };
    }
}
