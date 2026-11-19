namespace TaxVision.Tenant.Domain.Team;

/// <summary>Quién ve una entrada, además de pasar el acceso a la actividad del miembro.</summary>
public enum ActivityVisibility
{
    /// <summary>Cualquiera que pueda ver la actividad del miembro.</summary>
    Team = 0,

    /// <summary>Solo con el permiso de lectura del módulo (facturas: <c>invoicing.view</c>).</summary>
    Restricted = 1,

    /// <summary>Solo el autor o quien ve todo el módulo (notas: <c>notes.view_all</c>).</summary>
    Private = 2,
}
