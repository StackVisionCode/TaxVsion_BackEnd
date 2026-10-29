namespace TaxVision.Tasks.ClientForms.Domain.Requests;

// Resultado por archivo de pasar un adjunto del participante externo al cliente vinculado.
public static class FormFileReassignmentOutcomes
{
    public const string Reassigned = "Reassigned";
    public const string AlreadyApplied = "AlreadyApplied";
    public const string NotFound = "NotFound";
    public const string OwnerMismatch = "OwnerMismatch";
    public const string LegalHold = "LegalHold";

    public static bool IsKnown(string? outcome) =>
        outcome is Reassigned or AlreadyApplied or NotFound or OwnerMismatch or LegalHold;
}
