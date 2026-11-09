using TaxVision.Tasks.ClientForms.Domain.Requests.Events;
using TaxVision.Tasks.ClientForms.Domain.Templates.Events;

namespace TaxVision.Tasks.ClientForms.Application.Common;

/// <summary>
/// Eventos de dominio de Client Forms sin consumidor a propósito: sus avisos los publican a mano los handlers
/// de comando. Sin esto quedarían sin handler en la cola domain-events.
/// </summary>
public static class UnconsumedClientFormsDomainEventHandler
{
    public static void Handle(FormRequestAssigned _) { }

    public static void Handle(FormRequestSubmitted _) { }

    public static void Handle(FormRequestReturned _) { }

    public static void Handle(FormRequestCompleted _) { }

    public static void Handle(FormRequestCancelled _) { }

    public static void Handle(FormRequestDueDateChanged _) { }

    public static void Handle(FormAttachmentRejected _) { }

    public static void Handle(FormTemplateCreated _) { }

    public static void Handle(FormTemplateRenamed _) { }

    public static void Handle(FormTemplateDraftUpdated _) { }

    public static void Handle(FormTemplatePublished _) { }

    public static void Handle(FormTemplateArchived _) { }

    public static void Handle(FormTemplateRestored _) { }

    public static void Handle(FormTemplateDraftReverted _) { }
}
