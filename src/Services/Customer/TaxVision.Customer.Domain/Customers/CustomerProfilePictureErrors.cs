using BuildingBlocks.Results;

namespace TaxVision.Customer.Domain.Customers;

/// <summary>Errores de la foto de perfil del cliente. Los mensajes llegan al usuario: en inglés.</summary>
public static class CustomerProfilePictureErrors
{
    public static readonly Error Archived = new(
        "Customer.Archived",
        "Archived customers cannot change their profile picture."
    );

    public static readonly Error FileIdRequired = new("Customer.ProfilePicture.FileId", "A file id is required.");

    /// <summary>El resultado del escaneo llegó para una foto que ya no es la pendiente.</summary>
    public static readonly Error Stale = new(
        "Customer.ProfilePicture.Stale",
        "The uploaded picture is no longer the pending one."
    );
}
