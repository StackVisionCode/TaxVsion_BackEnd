namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// De qué correo saliente se habla en un listado: el archivado en un cliente, o el que no se archivó
/// en ninguno (<see cref="Unlinked"/>). Explícito a propósito: un <c>Guid?</c> suelto haría que un id
/// faltante por error se leyera como "sin cliente" y mostrara otro correo. Es clase y no struct por
/// lo mismo: el <c>default</c> de un struct sería "sin cliente" sin que nadie lo pidiera.
/// </summary>
public sealed record DraftCustomerScope
{
    private DraftCustomerScope(Guid? customerId) => CustomerId = customerId;

    /// <summary>Null solo en <see cref="Unlinked"/>.</summary>
    public Guid? CustomerId { get; }

    public bool IsUnlinked => CustomerId is null;

    /// <summary>Correo nuevo que no se archivó en ningún cliente.</summary>
    public static DraftCustomerScope Unlinked => new(null);

    public static DraftCustomerScope ForCustomer(Guid customerId) => new(customerId);

    /// <summary>Un id de cliente es siempre el alcance de ese cliente; "sin cliente" se pide con <see cref="Unlinked"/>.</summary>
    public static implicit operator DraftCustomerScope(Guid customerId) => ForCustomer(customerId);
}
