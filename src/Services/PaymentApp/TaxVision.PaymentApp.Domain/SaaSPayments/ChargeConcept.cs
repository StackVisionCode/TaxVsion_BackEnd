using BuildingBlocks.Results;

namespace TaxVision.PaymentApp.Domain.SaaSPayments;

// Qué concepto cobró este pago, en términos que el cliente lee en el recibo. Lo que PaymentApp
// expone al Documents service para que el PDF diga "Storage +10 GB" en vez de "Add-on". Opcional
// — pagos legacy sin concepto caen al mapping genérico por PaymentType en el consumer.
public sealed record ChargeConcept
{
    public string Code { get; }
    public string DisplayName { get; }

    private ChargeConcept(string code, string displayName)
    {
        Code = code;
        DisplayName = displayName;
    }

    public static Result<ChargeConcept> Create(string code, string displayName)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 50)
            return Result.Failure<ChargeConcept>(
                new Error("ChargeConcept.InvalidCode", "Code is required and must be 50 characters or fewer.")
            );
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 200)
            return Result.Failure<ChargeConcept>(
                new Error("ChargeConcept.InvalidName", "DisplayName is required and must be 200 characters or fewer.")
            );
        return Result.Success(new ChargeConcept(code.Trim(), displayName.Trim()));
    }
}
