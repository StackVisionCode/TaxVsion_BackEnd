namespace BuildingBlocks.Domain;

/// <summary>
/// Qué le pasa a una referencia a un empleado cuando se lo retira (offboard). Ver
/// <see cref="UserReferenceAttribute"/>.
/// </summary>
public enum UserReferenceKind
{
    /// <summary>Procedencia, auditoría o dato legal: el retiro no lo toca nunca.</summary>
    Provenance,

    /// <summary>Trabajo activo: el retiro lo reasigna al sucesor o lo cierra.</summary>
    ActiveWork,

    /// <summary>Secreto o acceso del empleado: el retiro lo revoca.</summary>
    Credential,

    /// <summary>Algo personal del empleado: el retiro lo cancela o archiva.</summary>
    Private,

    /// <summary>Copia derivada de Auth (directorio, permisos): el retiro la marca inactiva.</summary>
    Projection,
}

/// <summary>
/// Declara que la propiedad apunta a un usuario de staff y qué hace el retiro con ella. Lo exige
/// <c>UserReferenceContractTests</c> (BuildingBlocks.Tests) para toda propiedad Guid de dominio con
/// nombre de referencia a usuario. <see cref="HandledBy"/> es obligatorio salvo en
/// <see cref="UserReferenceKind.Provenance"/>: nombre de la clase del mismo servicio que maneja
/// <c>UserOffboardedIntegrationEvent</c> (texto, porque Domain no ve la capa Application).
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class UserReferenceAttribute(UserReferenceKind kind) : Attribute
{
    public UserReferenceKind Kind { get; } = kind;

    /// <summary>
    /// Clase que la maneja en el retiro (p. ej. "CustomerUserOffboardedConsumer"), o "Servicio:Clase" cuando
    /// la corrige el retiro de otro servicio por eventos (entonces <see cref="Via"/> es obligatorio).
    /// </summary>
    public string? HandledBy { get; set; }

    /// <summary>Evento(s) derivado(s) que este servicio consume para corregirla ("A|B"), con HandledBy "Servicio:Clase".</summary>
    public string? Via { get; set; }

    /// <summary>Aclaración breve opcional.</summary>
    public string? Note { get; set; }
}
