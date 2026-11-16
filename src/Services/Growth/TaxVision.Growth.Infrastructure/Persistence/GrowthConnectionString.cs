using Microsoft.Data.SqlClient;

namespace TaxVision.Growth.Infrastructure.Persistence;

/// <summary>
/// Growth se conecta siempre sin MARS: con MARS, EF Core apaga los savepoints y el ejecutor de idempotencia los
/// necesita dentro de la transacción del handler (sin ellos responde <c>Growth.Idempotency.SavepointsRequired</c>).
/// Se fuerza aquí para no depender de cómo venga la cadena de cada entorno; Growth no lee en paralelo por conexión.
/// </summary>
public static class GrowthConnectionString
{
    public static string WithoutMars(string connectionString) =>
        new SqlConnectionStringBuilder(connectionString) { MultipleActiveResultSets = false }.ConnectionString;
}
