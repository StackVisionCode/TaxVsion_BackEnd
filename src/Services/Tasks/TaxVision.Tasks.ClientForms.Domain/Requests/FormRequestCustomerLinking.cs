using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Requests;

/// <summary>Vincular a un cliente el formulario de un participante externo. Parte del agregado <see cref="FormRequest"/>.</summary>
public sealed class FormRequestCustomerLinking
{
    private readonly FormRequest _request;

    internal FormRequestCustomerLinking(FormRequest request) => _request = request;

    public Result<CustomerLinkResult> TryAttach(
        Guid customerId,
        bool customerHasOpenRequestForTemplate,
        Guid? conflictingRequestId,
        DateTime nowUtc
    )
    {
        if (_request.ParticipantKind != FormParticipantKind.External || customerId == Guid.Empty)
            return Result.Failure<CustomerLinkResult>(FormRequestErrors.InvalidParticipant);
        if (_request.CustomerLinkStatus == CustomerLinkStatus.Linked)
            return _request.CustomerId == customerId
                ? Result.Success(CustomerLinkResult.Unchanged)
                : Result.Failure<CustomerLinkResult>(FormRequestErrors.AlreadyLinked);

        if (customerHasOpenRequestForTemplate)
        {
            _request.CustomerId = null;
            _request.CustomerLinkStatus = CustomerLinkStatus.Blocked;
            _request.LinkBlockedCustomerId = customerId;
            _request.LinkBlockedReason = "customer_has_open_request";
            _request.LinkConflictingRequestId = conflictingRequestId;
            _request.Touch(nowUtc);
            return Result.Success(CustomerLinkResult.Blocked);
        }

        _request.CustomerId = customerId;
        _request.CustomerLinkStatus = CustomerLinkStatus.Linked;
        _request.LinkBlockedCustomerId = null;
        _request.LinkBlockedReason = null;
        _request.LinkConflictingRequestId = null;
        _request.Touch(nowUtc);
        return Result.Success(CustomerLinkResult.Linked);
    }

    public Result<CustomerLinkResult> Retry(
        bool customerHasOpenRequestForTemplate,
        Guid? conflictingRequestId,
        DateTime nowUtc
    )
    {
        if (_request.CustomerLinkStatus != CustomerLinkStatus.Blocked || _request.LinkBlockedCustomerId is null)
            return Result.Failure<CustomerLinkResult>(FormRequestErrors.LinkNotBlocked);
        return TryAttach(
            _request.LinkBlockedCustomerId.Value,
            customerHasOpenRequestForTemplate,
            conflictingRequestId,
            nowUtc
        );
    }
}
