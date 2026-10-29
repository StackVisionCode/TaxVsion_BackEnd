using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.Avatars;

namespace TaxVision.Customer.Application.Customers.Portal;

/// <summary>Teléfono, email y foto de la ficha del propio cliente (la misma que edita su preparador).</summary>
public static class GetMyCustomerContactHandler
{
    public static async Task<Result<MyCustomerContactResponse>> Handle(
        GetMyCustomerContactQuery query,
        ICustomerRepository repository,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(query.CustomerId, ct);
        if (customer is null || customer.TenantId != query.TenantId)
            return Result.Failure<MyCustomerContactResponse>(new Error("Customer.NotFound", "Customer not found."));

        var upload = CustomerAvatarStatus.Of(
            customer.PendingProfilePictureFileId,
            customer.PendingProfilePictureRequestedAtUtc,
            time.GetUtcNow().UtcDateTime
        );

        return Result.Success(
            new MyCustomerContactResponse(
                customer.PrimaryPhone?.E164Value,
                customer.PrimaryEmail.Value,
                customer.ProfilePictureFileId,
                upload.PendingAvatarFileId,
                upload.UploadStatus
            )
        );
    }
}
