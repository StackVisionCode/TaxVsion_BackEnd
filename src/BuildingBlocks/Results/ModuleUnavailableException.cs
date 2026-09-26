namespace BuildingBlocks.Results;

/// <summary>El permiso pertenece a un módulo que el plan del tenant no habilita. La lanza el gate de
/// módulo en modo enforce; el middleware la mapea a 403.</summary>
public sealed class ModuleUnavailableException(string code, string message, string? module = null) : Exception(message)
{
    public string Code { get; } = code;

    /// <summary>
    /// Código del módulo que falta (<c>campaigns</c>, <c>comms</c>…). Va en el cuerpo del 403 para que
    /// el frontend pueda decir "tu plan no incluye X" sin parsear el mensaje ni mantener su propia copia
    /// del mapa permiso → módulo (A5: contrato de errores RFC 9457).
    /// </summary>
    public string? Module { get; } = module;
}
