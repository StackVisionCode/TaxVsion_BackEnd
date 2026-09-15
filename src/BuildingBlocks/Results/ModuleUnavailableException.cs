namespace BuildingBlocks.Results;

/// <summary>El permiso pertenece a un módulo que el plan del tenant no habilita. La lanza el gate de
/// módulo en modo enforce; el middleware la mapea a 403.</summary>
public sealed class ModuleUnavailableException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
