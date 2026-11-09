using TaxVision.Codes.Domain.Events;

namespace TaxVision.Growth.Api.DomainEvents;

/// <summary>
/// Eventos sin consumidor a propósito: hoy nadie los necesita fuera de Growth y no tienen aviso de integración.
/// Cuesta una fila en wolverine_incoming_envelopes por evento. Al activar uno, sale de esta lista.
/// </summary>
public static class UnconsumedCodeReservationDomainEventHandler
{
    public static void Handle(CodeReservationCreatedDomainEvent _) { }

    public static void Handle(CodeReservationCancelledDomainEvent _) { }

    public static void Handle(CodeReservationExpiredDomainEvent _) { }

    public static void Handle(CodeReservationCompensatedDomainEvent _) { }
}
