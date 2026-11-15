namespace BuildingBlocks.TimeZones;

/// <summary>Qué hacer con una hora local que no existe (el salto del horario de verano). Política de Calendar (C-T9).</summary>
public enum NonexistentTimePolicy
{
    /// <summary>Al crear: el usuario eligió una hora imposible y se le dice.</summary>
    Reject,

    /// <summary>Al expandir o calcular: se corre hacia adelante el largo del salto.</summary>
    ShiftForward,
}
