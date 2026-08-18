using BuildingBlocks.Results;

namespace TaxVision.Tasks.Domain.ClientRequests;

public static class ClientRequestErrors
{
    public static readonly Error NotFound = new("ClientRequest.NotFound", "The client request was not found.");

    public static readonly Error CustomerRequired = new(
        "ClientRequest.CustomerRequired",
        "A client request always belongs to a customer."
    );

    public static readonly Error TitleRequired = new(
        "ClientRequest.TitleRequired",
        "Tell the client what you are asking for."
    );

    public static readonly Error TitleTooLong = new("ClientRequest.TitleTooLong", "The title exceeds 200 characters.");

    public static readonly Error DueNotUtc = new("ClientRequest.DueNotUtc", "The due date must be UTC.");

    public static readonly Error Closed = new(
        "ClientRequest.Closed",
        "This request is already resolved and takes no more documents."
    );

    public static readonly Error NothingSubmitted = new(
        "ClientRequest.NothingSubmitted",
        "There is nothing submitted to accept or reject yet."
    );

    public static readonly Error RejectionReasonRequired = new(
        "ClientRequest.RejectionReasonRequired",
        "Say what is wrong: a bare rejection leaves the client with nothing to fix."
    );

    public static readonly Error FileRequired = new("ClientRequest.FileRequired", "A file id is required.");

    public static readonly Error FileNameRequired = new(
        "ClientRequest.FileNameRequired",
        "The document display name is required."
    );

    public static readonly Error FileNameTooLong = new(
        "ClientRequest.FileNameTooLong",
        "The document display name exceeds 260 characters."
    );

    public static readonly Error DuplicateDocument = new(
        "ClientRequest.DuplicateDocument",
        "That file was already submitted for this request."
    );

    public static readonly Error TooManyDocuments = new(
        "ClientRequest.TooManyDocuments",
        "A request cannot hold more than 20 documents."
    );

    /// <summary>El pedido existe, pero es de otro cliente. Se responde 404 para no confirmarlo.</summary>
    public static readonly Error NotYours = new("ClientRequest.NotFound", "The client request was not found.");
}
