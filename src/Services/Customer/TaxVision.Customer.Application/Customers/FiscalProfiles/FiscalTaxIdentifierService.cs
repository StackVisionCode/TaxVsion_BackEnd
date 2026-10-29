using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.FiscalProfiles;

namespace TaxVision.Customer.Application.Customers.FiscalProfiles;

// Camino fiscal del SSN/EIN compartido por el handler HTTP y el consumer de formularios: normaliza, valida,
// cifra, calcula blind index y last4, y verifica unicidad. Extraído para no duplicar la validación.
// BlindIndexCandidates: el BI actual (= BlindIndex, siempre primero) más el de la clave anterior durante la
// ventana de rotación, para el chequeo de unicidad sin falsos negativos. KeyVersion: la versión que se estampa.
public sealed record EncryptedTaxIdentifier(
    byte[] Cipher,
    string BlindIndex,
    string Last4,
    IReadOnlyCollection<string> BlindIndexCandidates,
    short KeyVersion
);

public static class FiscalTaxIdentifierService
{
    public static Result<string> ValidateAndNormalize(string raw, FiscalSubjectKind kind)
    {
        var normalized = Normalize(raw);
        if (!IsValid(normalized, kind))
            return Result.Failure<string>(
                new Error("FiscalProfile.TaxId", "Tax identifier format is invalid for the subject kind.")
            );
        return Result.Success(normalized);
    }

    public static EncryptedTaxIdentifier Encrypt(string normalized, Guid tenantId, ISensitiveDataProtector protector)
    {
        var candidates = protector.ComputeBlindIndexCandidates(normalized, tenantId);
        return new EncryptedTaxIdentifier(
            protector.Protect(normalized),
            candidates.First(), // el actual va primero: es el que se almacena
            normalized[^4..],
            candidates,
            protector.CurrentKeyVersion
        );
    }

    public static async Task<Result> EnsureUniqueAsync(
        ICustomerRepository repository,
        Guid tenantId,
        IReadOnlyCollection<string> blindIndexCandidates,
        Guid currentCustomerId,
        CancellationToken ct
    )
    {
        var conflicting = await repository.FindCustomerIdByFiscalBlindIndexAsync(
            tenantId,
            blindIndexCandidates,
            excludeCustomerId: currentCustomerId,
            ct
        );
        return conflicting is null
            ? Result.Success()
            : Result.Failure(
                new Error(
                    "FiscalProfile.TaxIdentifierAlreadyExists",
                    "Another customer in this tenant already has this tax identifier."
                )
            );
    }

    // Mapea el kind del formulario (Ssn/Itin/Ein) al sujeto fiscal real de Customer.
    public static FiscalSubjectKind SubjectKindFor(string? formKind) =>
        string.Equals(formKind, "Ein", StringComparison.OrdinalIgnoreCase)
            ? FiscalSubjectKind.Business
            : FiscalSubjectKind.Individual;

    private static string Normalize(string raw) =>
        string.IsNullOrWhiteSpace(raw) ? string.Empty : new string(raw.Where(char.IsDigit).ToArray());

    private static bool IsValid(string normalized, FiscalSubjectKind kind) =>
        kind switch
        {
            // SSN/ITIN: 9 dígitos, no empieza con 000 ni 666.
            FiscalSubjectKind.Individual => normalized.Length == 9
                && !normalized.StartsWith("000", StringComparison.Ordinal)
                && !normalized.StartsWith("666", StringComparison.Ordinal),
            // EIN: 9 dígitos.
            FiscalSubjectKind.Business => normalized.Length == 9,
            _ => false,
        };
}
