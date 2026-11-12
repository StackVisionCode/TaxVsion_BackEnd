namespace TaxVision.Subscription.Domain.Subscriptions;

public enum PlanChangeLineKind
{
    Plan,
    AddOn,
    Seats,
}

/// <summary>
/// Una línea de lo que cobra un cambio de plan o de ciclo: el plan, cada add-on o los asientos extra de un tipo.
/// Se guarda con la solicitud para aplicar al pagar exactamente lo cobrado y para que el recibo lo detalle.
/// </summary>
public sealed class PlanChangeLine
{
    public const int CodeMaxLength = 64;
    public const int DescriptionMaxLength = 200;

    private PlanChangeLine() { }

    public Guid Id { get; private set; }
    public PlanChangeLineKind Kind { get; private set; }

    /// <summary>El add-on contratado (TenantAddOn) que cambia; null para el plan y los asientos.</summary>
    public Guid? ReferenceId { get; private set; }

    /// <summary>Código del plan, del add-on o el tipo de asiento.</summary>
    public string Code { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public int Quantity { get; private set; }
    public long UnitAmountCents { get; private set; }

    public long AmountCents => Quantity * UnitAmountCents;

    public static PlanChangeLine ForPlan(string planCode, string description, long amountCents) =>
        New(PlanChangeLineKind.Plan, null, planCode, description, 1, amountCents);

    public static PlanChangeLine ForAddOn(
        Guid tenantAddOnId,
        string addOnCode,
        string description,
        int quantity,
        long unitAmountCents
    ) => New(PlanChangeLineKind.AddOn, tenantAddOnId, addOnCode, description, quantity, unitAmountCents);

    public static PlanChangeLine ForSeats(string seatType, string description, int quantity, long unitAmountCents) =>
        New(PlanChangeLineKind.Seats, null, seatType, description, quantity, unitAmountCents);

    private static PlanChangeLine New(
        PlanChangeLineKind kind,
        Guid? referenceId,
        string code,
        string description,
        int quantity,
        long unitAmountCents
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            ReferenceId = referenceId,
            Code = code.Length > CodeMaxLength ? code[..CodeMaxLength] : code,
            Description = description.Length > DescriptionMaxLength ? description[..DescriptionMaxLength] : description,
            Quantity = quantity,
            UnitAmountCents = unitAmountCents,
        };
}
