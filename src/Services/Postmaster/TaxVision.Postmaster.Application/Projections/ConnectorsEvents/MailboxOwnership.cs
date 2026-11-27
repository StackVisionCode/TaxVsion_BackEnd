namespace TaxVision.Postmaster.Application.Projections.ConnectorsEvents;

/// <summary>
/// SR-06: "de oficina" lo declara Connectors (<c>IsOffice</c>); no se infiere del dueño nulo. Oficina = declarada y sin
/// dueño; personal = no oficina y con dueño. Lo demás (dato viejo o contradictorio) queda sin conocer y no se usa.
/// </summary>
public static class MailboxOwnership
{
    public static (bool Known, Guid? OwnerUserId) Resolve(Guid? ownerUserId, bool? isOffice) =>
        (isOffice, ownerUserId) switch
        {
            (true, null) => (true, null),
            (false, { } owner) => (true, owner),
            _ => (false, null),
        };
}
