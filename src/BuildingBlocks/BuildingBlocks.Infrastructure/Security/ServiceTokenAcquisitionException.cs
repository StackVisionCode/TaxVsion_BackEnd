namespace BuildingBlocks.Infrastructure.Security;

/// <summary>F25 — fallo esperado al pedirle a Auth un token M2M (red caída, timeout, respuesta
/// inválida). Los ~9 acquirers por servicio la atrapan y devuelven <c>null</c> a su caller, igual
/// que hacían antes de la consolidación.</summary>
public sealed class ServiceTokenAcquisitionException(string message, Exception? innerException = null)
    : Exception(message, innerException);
