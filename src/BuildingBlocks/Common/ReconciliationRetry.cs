namespace BuildingBlocks.Common;

/// <summary>
/// Espera entre pasadas de una reconciliación de respaldo. Si la pasada no terminó (Auth, Tenant o Customer aún no
/// responden, por ejemplo al desplegar), se reintenta en minutos en vez de esperar el intervalo completo.
/// </summary>
public static class ReconciliationRetry
{
    public static readonly TimeSpan AfterFailure = TimeSpan.FromMinutes(2);

    public static TimeSpan NextDelay(bool completed, TimeSpan interval) =>
        completed || interval <= AfterFailure ? interval : AfterFailure;
}
