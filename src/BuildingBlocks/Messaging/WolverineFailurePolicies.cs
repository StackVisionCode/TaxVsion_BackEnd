using System.Text.Json;
using Wolverine;
using Wolverine.ErrorHandling;

namespace BuildingBlocks.Messaging;

/// <summary>
/// H-15. Política única de fallo del bus para los 17 servicios .NET.
///
/// <para>
/// Antes, los 17 <c>Program.cs</c> repetían literalmente
/// <c>Policies.OnException&lt;Exception&gt;().RetryWithCooldown(1s, 5s, 15s)</c>: <b>todo</b> se
/// reintentaba 4 veces, incluidos los fallos deterministas. Reintentar un bug no lo arregla —
/// gasta 21 segundos y tres ejecuciones más del handler para llegar exactamente al mismo sitio,
/// y mientras tanto el mensaje ocupa un slot del consumidor.
/// </para>
///
/// <para>
/// Aquí se distingue <b>permanente</b> de <b>transitorio</b>. Lo permanente va directo a la
/// dead-letter queue; todo lo demás conserva el comportamiento anterior sin cambios.
/// </para>
///
/// <para>
/// <b>Orden verificado contra el broker real</b> (Wolverine 6.14 + RabbitMQ): gana la primera
/// regla que matchea, así que las específicas tienen que registrarse antes de la genérica. Medido:
/// una <see cref="JsonException"/> con este orden da 1 intento y aterriza en
/// <c>wolverine-dead-letter-queue</c>; una <see cref="InvalidOperationException"/> sigue dando 3.
/// </para>
///
/// <para>
/// La DLQ está vigilada desde H-16 (alerta Grafana a los 5 minutos con cualquier mensaje muerto,
/// runbook de drenado en README §48). Ese es el motivo por el que mandar algo ahí ya no es
/// perderlo de vista, y por el que este cambio es seguro.
/// </para>
/// </summary>
public static class WolverineFailurePolicies
{
    /// <summary>
    /// Excepciones que <b>no</b> pueden mejorar reintentando: dependen solo del payload, que es
    /// idéntico en cada intento.
    ///
    /// <para>
    /// La lista es corta a propósito. El criterio para entrar es "no existe ningún escenario
    /// plausible en el que el segundo intento vaya mejor que el primero". Tres candidatos obvios
    /// se quedaron fuera por fallar ese criterio bajo consistencia eventual, que es el modo normal
    /// de este bus:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <c>InvalidOperationException</c> — EF Core la lanza para casos muy distintos, incluido
    ///     "sequence contains no elements" cuando una proyección todavía no llegó. Eso sí se
    ///     arregla reintentando.
    ///   </description></item>
    ///   <item><description>
    ///     <c>NullReferenceException</c> — tienta, porque siempre es un bug. Pero el bug puede ser
    ///     "leí una proyección que aún no existe", y ahí el reintento es exactamente lo correcto.
    ///   </description></item>
    ///   <item><description>
    ///     <c>KeyNotFoundException</c> — mismo motivo.
    ///   </description></item>
    /// </list>
    /// </summary>
    private static readonly Type[] PermanentFailures =
    [
        // El payload no se puede deserializar. Los bytes son los mismos en el reintento.
        typeof(JsonException),
        // Cubre ArgumentNullException y ArgumentOutOfRangeException: el evento trae un campo
        // que el handler considera inválido, y va a seguir trayéndolo.
        typeof(ArgumentException),
        // Un Guid, fecha o enum del payload que no parsea.
        typeof(FormatException),
        typeof(NotSupportedException),
    ];

    /// <summary>
    /// Reintentos de lo transitorio. Son los mismos tres cooldowns de siempre (4 intentos en
    /// total) — este helper no cambia el comportamiento de nada que no esté en
    /// <see cref="PermanentFailures"/>.
    /// </summary>
    private static readonly TimeSpan[] Cooldowns =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
    ];

    /// <summary>
    /// Aplica la política de fallo estándar. Se llama una vez por servicio, dentro de
    /// <c>UseWolverine(options =&gt; ...)</c>.
    /// </summary>
    public static WolverineOptions ApplyStandardFailurePolicies(this WolverineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var permanent in PermanentFailures)
            options.Policies.OnExceptionOfType(permanent).MoveToErrorQueue();

        options.Policies.OnException<Exception>().RetryWithCooldown(Cooldowns);

        return options;
    }

    /// <summary>
    /// Las excepciones tratadas como permanentes. Expuesto para que la fitness function pueda
    /// afirmar sobre la lista sin duplicarla.
    /// </summary>
    public static IReadOnlyList<Type> PermanentFailureTypes => PermanentFailures;
}
