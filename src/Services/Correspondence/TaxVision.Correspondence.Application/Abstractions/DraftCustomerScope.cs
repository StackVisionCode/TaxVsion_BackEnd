namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// De qué correo saliente se habla en un listado: el archivado en un cliente, o el que no se archivó
/// en ninguno (<see cref="Unlinked"/>). Explícito a propósito: un <c>Guid?</c> suelto haría que un id
/// faltante por error se leyera como "sin cliente" y mostrara otro correo. Es clase y no struct por
/// lo mismo: el <c>default</c> de un struct sería "sin cliente" sin que nadie lo pidiera.
/// </summary>
public sealed record DraftCustomerScope
{
    private DraftCustomerScope(Guid? customerId, bool isAll, Guid? restrictToUserId)
    {
        CustomerId = customerId;
        IsAll = isAll;
        RestrictToUserId = restrictToUserId;
    }

    /// <summary>Null en <see cref="Unlinked"/> y en <see cref="All"/>.</summary>
    public Guid? CustomerId { get; }

    /// <summary>Todo el correo saliente: sin cliente y de los clientes que el usuario puede ver.</summary>
    public bool IsAll { get; }

    public bool IsUnlinked => !IsAll && CustomerId is null;

    /// <summary>
    /// Null = ve todos los clientes (<c>customers.view_all</c>); si no, el correo de cliente se acota a los
    /// asignados a este usuario. No afecta al correo sin cliente.
    /// </summary>
    public Guid? RestrictToUserId { get; }

    /// <summary>Correo nuevo que no se archivó en ningún cliente.</summary>
    public static DraftCustomerScope Unlinked => new(null, false, null);

    public static DraftCustomerScope ForCustomer(Guid customerId) => new(customerId, false, null);

    /// <summary>Carpetas Sent, Drafts y Trash sin elegir cliente: cada fila dice de qué cliente es (o ninguno).</summary>
    public static DraftCustomerScope All => new(null, true, null);

    /// <summary>El mismo alcance acotado a los clientes asignados de <paramref name="userId"/> (null = sin acotar).</summary>
    public DraftCustomerScope RestrictedTo(Guid? userId) => new(CustomerId, IsAll, userId);

    /// <summary>Un id de cliente es siempre el alcance de ese cliente; "sin cliente" se pide con <see cref="Unlinked"/>.</summary>
    public static implicit operator DraftCustomerScope(Guid customerId) => ForCustomer(customerId);
}
