using BuildingBlocks.Messaging;

namespace TaxVision.Signature.Application.Sealing;

/// <summary>
/// Otro nodo tiene el candado de sellado de la solicitud. No se descarta el mensaje: la política estándar de
/// BB-7 (<see cref="DependencyNotReadyException"/>) lo vuelve a programar y, al agotarse, va a la cola de
/// errores. El job de reconciliación de sellado cubre lo que quede.
/// </summary>
public sealed class SealingLockBusyException(string message) : DependencyNotReadyException("SealingLock", message);
