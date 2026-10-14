namespace TaxVision.CloudStorage.Application.Abstractions;

/// <summary>
/// Flag de visibilidad por asignación (acceso a clientes) para CloudStorage. Cuando <see cref="Enabled"/>
/// es true, un empleado sin <c>customers.view_all</c> solo ve folders/files cuyo OwnerType=Customer está
/// entre sus clientes asignados; portal (<c>IsCustomerPortal</c>) no se toca. Default FALSE — se puede
/// desplegar sin cortar acceso hasta terminar la siembra de la proyección. Espejo de
/// Signature:AssignmentVisibility.
/// </summary>
public sealed class CloudStorageVisibilityOptions
{
    public const string SectionName = "CloudStorage:AssignmentVisibility";

    public bool Enabled { get; set; }
}
