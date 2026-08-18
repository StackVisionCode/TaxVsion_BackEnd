using TaxVision.Tasks.Domain.ClientRequests;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Tests.ClientRequests;

public sealed class ClientRequestTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid PreparerId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_new_request_starts_pending()
    {
        var request = NewRequest();

        Assert.Equal(ClientRequestStatus.Pending, request.Status);
        Assert.True(request.IsOpen);
    }

    /// <summary>
    /// El cliente sube y el pedido queda «mandó algo», no «ya está». Cerrarlo es del preparador: el
    /// mismo criterio por el que nada saca una tarea de WaitingOnClient automáticamente.
    /// </summary>
    [Fact]
    public void Submitting_a_document_never_accepts_the_request()
    {
        var request = NewRequest();

        request.SubmitDocument(Guid.NewGuid(), "w2.pdf", "application/pdf", 2048, Now);

        Assert.Equal(ClientRequestStatus.Submitted, request.Status);
        Assert.NotEqual(ClientRequestStatus.Accepted, request.Status);
    }

    /// <summary>Viene de fuera de la firma: el veredicto lo da el escaneo, no quien sube.</summary>
    [Fact]
    public void A_submitted_document_waits_for_the_scan()
    {
        var request = NewRequest();

        var document = request.SubmitDocument(Guid.NewGuid(), "w2.pdf", "application/pdf", 2048, Now);

        Assert.Equal(AttachmentStatus.Pending, document.Value.Status);
    }

    [Fact]
    public void Submitting_raises_the_event_only_the_first_time()
    {
        var request = NewRequest();

        request.SubmitDocument(Guid.NewGuid(), "w2.pdf", null, 10, Now);
        request.SubmitDocument(Guid.NewGuid(), "1099.pdf", null, 10, Now);

        var submitted = request.DomainEvents.Count(e => e.GetType().Name == "ClientRequestSubmittedDomainEvent");
        Assert.Equal(1, submitted);
    }

    [Fact]
    public void Accepting_before_anything_arrives_is_rejected()
    {
        var request = NewRequest();

        var accepted = request.Accept(PreparerId, null, Now);

        Assert.Equal(ClientRequestErrors.NothingSubmitted, accepted.Error);
    }

    /// <summary>Un «rechazado» a secas deja al cliente sin saber qué corregir.</summary>
    [Fact]
    public void Rejecting_without_a_reason_is_rejected()
    {
        var request = NewRequest();
        request.SubmitDocument(Guid.NewGuid(), "w2.pdf", null, 10, Now);

        var rejected = request.Reject(PreparerId, "   ", Now);

        Assert.Equal(ClientRequestErrors.RejectionReasonRequired, rejected.Error);
    }

    [Fact]
    public void A_resolved_request_takes_no_more_documents()
    {
        var request = NewRequest();
        request.SubmitDocument(Guid.NewGuid(), "w2.pdf", null, 10, Now);
        request.Accept(PreparerId, null, Now);

        var late = request.SubmitDocument(Guid.NewGuid(), "tarde.pdf", null, 10, Now);

        Assert.Equal(ClientRequestErrors.Closed, late.Error);
    }

    /// <summary>
    /// Si el escaneo tumba el único archivo, el pedido vuelve a pendiente: la lista del cliente tiene
    /// que decirle que todavía le falta.
    /// </summary>
    [Fact]
    public void A_rejected_scan_puts_the_request_back_to_pending()
    {
        var request = NewRequest();
        var fileId = Guid.NewGuid();
        request.SubmitDocument(fileId, "eicar.txt", null, 68, Now);

        var rejected = request.MarkDocumentRejected(fileId, "infected", Now);

        Assert.True(rejected);
        Assert.Equal(ClientRequestStatus.Pending, request.Status);
    }

    /// <summary>El motivo técnico viaja en el evento, para el preparador; el cliente no lo ve.</summary>
    [Fact]
    public void The_rejection_event_carries_the_real_reason_and_the_preparer()
    {
        var request = NewRequest();
        var fileId = Guid.NewGuid();
        request.SubmitDocument(fileId, "eicar.txt", null, 68, Now);

        request.MarkDocumentRejected(fileId, "infected", Now);

        var raised = request.DomainEvents.Single(e => e.GetType().Name == "ClientRequestDocumentRejectedDomainEvent");
        var reason = raised.GetType().GetProperty("Reason")!.GetValue(raised);
        var notified = raised.GetType().GetProperty("RequestedByUserId")!.GetValue(raised);

        Assert.Equal("infected", reason);
        Assert.Equal(PreparerId, notified);
    }

    [Fact]
    public void Marking_a_file_that_is_not_here_is_a_silent_no_op()
    {
        var request = NewRequest();

        Assert.False(request.MarkDocumentAvailable(Guid.NewGuid(), Now));
        Assert.False(request.MarkDocumentRejected(Guid.NewGuid(), "infected", Now));
        Assert.False(request.MarkDocumentDetached(Guid.NewGuid(), Now));
    }

    [Fact]
    public void The_same_file_cannot_be_submitted_twice()
    {
        var request = NewRequest();
        var fileId = Guid.NewGuid();
        request.SubmitDocument(fileId, "w2.pdf", null, 10, Now);

        var again = request.SubmitDocument(fileId, "w2-otra-vez.pdf", null, 10, Now);

        Assert.Equal(ClientRequestErrors.DuplicateDocument, again.Error);
    }

    [Fact]
    public void A_request_without_a_customer_is_rejected()
    {
        var orphan = ClientRequest.Create(TenantId, Guid.Empty, PreparerId, null, "W-2", null, null, Now);

        Assert.Equal(ClientRequestErrors.CustomerRequired, orphan.Error);
    }

    private static ClientRequest NewRequest() =>
        ClientRequest
            .Create(TenantId, CustomerId, PreparerId, Guid.NewGuid(), "W-2 y 1099 de 2025", null, null, Now)
            .Value;
}
