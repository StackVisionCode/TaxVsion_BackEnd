using System.Numerics;

namespace TaxVision.Gateway.LoadShedding;

/// <summary>
/// Ventana deslizante (por-segundo) de latencia propia del Gateway + tasa de 5xx. Una instancia por
/// proceso — la señal de sobrecarga es local a esta réplica, no agregada de flota (mismo criterio
/// que un local overload manager: cada réplica protege su propia capacidad, sin depender de un store
/// compartido nuevo).
///
/// <para>
/// GW-05 — la versión anterior guardaba <b>cada muestra de latencia</b> en una <c>List&lt;double&gt;</c>
/// por segundo (hasta 4.000 × 60 = 240.000 <c>double</c> ≈ 1,92 MB, arriba del umbral del LOH) y en
/// cada lectura hacía <c>Sort()</c> — ~4,3 millones de comparaciones — todo bajo un <b>lock global</b>
/// que serializaba el 100% del tráfico. Ese coste solo aparece con la ventana llena, o sea bajo carga
/// alta: el shedder se convertía en un amplificador de sobrecarga justo cuando debía actuar. Ahora es
/// un histograma log-lineal de tamaño fijo: <c>Record</c> es O(1) sin lock ni asignaciones, y el
/// cálculo del p99 recorre un número constante de buckets. El error relativo del p99 es ~1,6%, de
/// sobra contra un umbral de 2.000 ms.
/// </para>
/// </summary>
public sealed class RequestOutcomeWindow
{
    /// <summary>Sub-buckets por octava (potencia de 2). 32 → error relativo máximo ~3%, ~1,6% al
    /// tomar el punto medio.</summary>
    private const int SubBits = 5;
    private const int SubCount = 1 << SubBits;

    /// <summary>Cubre hasta 2^20 ms (~17 min); todo lo que pase se satura en el último bucket.</summary>
    private const int BucketCount = ((20 - SubBits + 1) << SubBits) + SubCount;

    private readonly int windowSeconds;
    private readonly Second[] ring;

    public RequestOutcomeWindow(int windowSeconds)
    {
        this.windowSeconds = windowSeconds;
        // Un slot extra para que el segundo en curso nunca comparta slot con el más viejo vigente.
        ring = new Second[windowSeconds + 1];
        for (var i = 0; i < ring.Length; i++)
            ring[i] = new Second();
    }

    public void Record(double latencyMs, int statusCode)
    {
        var now = CurrentSecond();
        var slot = ring[(int)(now % ring.Length)];

        slot.EnsureCurrent(now);

        Interlocked.Increment(ref slot.Counts[BucketIndex((long)latencyMs)]);
        Interlocked.Increment(ref slot.Total);
        if (statusCode >= 500)
            Interlocked.Increment(ref slot.Errors5xx);
    }

    public WindowSnapshot GetSnapshot()
    {
        var now = CurrentSecond();
        var cutoff = now - windowSeconds;
        Span<long> merged = stackalloc long[BucketCount];
        long total = 0;
        long errors5xx = 0;

        foreach (var slot in ring)
        {
            // Volatile.Read: el segundo se publica antes que los contadores se limpien, así que leerlo
            // rancio solo puede descartar un slot vigente, nunca incluir uno vencido.
            if (Volatile.Read(ref slot.SecondKey) <= cutoff)
                continue;

            total += Interlocked.Read(ref slot.Total);
            errors5xx += Interlocked.Read(ref slot.Errors5xx);
            for (var i = 0; i < BucketCount; i++)
                merged[i] += Interlocked.Read(ref slot.Counts[i]);
        }

        return total == 0
            ? new WindowSnapshot(0, 0, 0)
            : new WindowSnapshot(total, Percentile(merged, total, 0.99), (double)errors5xx / total);
    }

    private static double Percentile(ReadOnlySpan<long> buckets, long total, double percentile)
    {
        var target = (long)Math.Ceiling(total * percentile);
        long seen = 0;

        for (var i = 0; i < buckets.Length; i++)
        {
            seen += buckets[i];
            if (seen >= target)
                return BucketValue(i);
        }

        return BucketValue(buckets.Length - 1);
    }

    /// <summary>
    /// Lineal por debajo de <see cref="SubCount"/> ms (ahí cada bucket es 1 ms exacto) y log-lineal
    /// por encima: <c>SubCount</c> sub-buckets por octava. Monótono y contiguo, que es lo que permite
    /// recorrerlo en orden para el percentil.
    /// </summary>
    internal static int BucketIndex(long milliseconds)
    {
        if (milliseconds <= 0)
            return 0;
        if (milliseconds < SubCount)
            return (int)milliseconds;

        var msb = BitOperations.Log2((ulong)milliseconds);
        var shift = msb - SubBits;
        var sub = (int)((milliseconds >> shift) & (SubCount - 1));
        var index = ((msb - SubBits + 1) << SubBits) + sub;

        return Math.Min(index, BucketCount - 1);
    }

    /// <summary>Latencia representativa del bucket: su punto medio.</summary>
    internal static double BucketValue(int index)
    {
        if (index < SubCount)
            return index;

        var shift = (index >> SubBits) - 1;
        var lower = (long)(SubCount + (index & (SubCount - 1))) << shift;

        return lower + ((1L << shift) - 1) / 2.0;
    }

    private static long CurrentSecond() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Un segundo del anillo. Se recicla en sitio: nunca se asigna memoria en
    /// <see cref="Record"/>.</summary>
    private sealed class Second
    {
        public readonly long[] Counts = new long[BucketCount];
        public long Total;
        public long Errors5xx;
        public long SecondKey = long.MinValue;

        private readonly Lock resetGate = new();

        /// <summary>
        /// Limpia el slot si viene de una vuelta anterior del anillo. El lock solo se toma en el
        /// cambio de segundo (una vez por slot por vuelta), no en el camino común.
        /// </summary>
        public void EnsureCurrent(long second)
        {
            if (Volatile.Read(ref SecondKey) == second)
                return;

            lock (resetGate)
            {
                if (SecondKey == second)
                    return;

                Array.Clear(Counts);
                Total = 0;
                Errors5xx = 0;
                // Último: publica el slot ya limpio para los lectores.
                Volatile.Write(ref SecondKey, second);
            }
        }
    }
}

public readonly record struct WindowSnapshot(long SampleCount, double P99LatencyMs, double ErrorRate5xx);
