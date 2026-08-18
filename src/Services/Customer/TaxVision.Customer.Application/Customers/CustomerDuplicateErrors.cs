using BuildingBlocks.Results;

namespace TaxVision.Customer.Application.Customers;

/// <summary>
/// Los dos errores que impiden que el mismo cliente entre dos veces al mismo tenant. Viven juntos
/// porque las dos puertas —crear y editar— tienen que responder lo mismo.
/// </summary>
public static class CustomerDuplicateErrors
{
    /// <summary>
    /// Ya hay un cliente igual en este tenant. Lleva el id del existente para que quien llama pueda
    /// abrirlo, o reintentar pidiendo sobreescribirlo.
    /// </summary>
    public static Error DuplicateFound(Guid existingCustomerId, string existingDisplayName, string matchedBy) =>
        new(
            "Customer.DuplicateFound",
            $"A customer matching by {matchedBy} already exists in this tenant: "
                + $"{existingDisplayName} ({existingCustomerId})."
        );

    /// <summary>
    /// El correo es la llave del portal y del directorio: dos clientes con el mismo no se distinguen,
    /// ni acá ni en los siete servicios que proyectan el directorio.
    /// </summary>
    public static readonly Error EmailAlreadyInUse = new(
        "Customer.EmailAlreadyInUse",
        "Another customer in this tenant already uses that email."
    );
}
