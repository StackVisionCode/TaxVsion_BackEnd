using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace TaxVision.BuildingBlocks.Tests.Integration;

/// <summary>
/// RabbitMQ y Redis locales para las pruebas de caída: variables de entorno o los user-secrets de Tasks.Api
/// (los mismos que ubican la base de BuildingBlocksTestDatabase). Nunca se imprimen.
/// </summary>
internal static class LocalBrokers
{
    private static readonly IConfiguration Secrets = new ConfigurationBuilder()
        .AddUserSecrets("12c547c3-e9a6-462e-82c4-bc6f5b0d4e41")
        .Build();

    public static Uri RabbitUri =>
        new(
            Environment.GetEnvironmentVariable("BUILDINGBLOCKS_RABBITMQ_URI")
                ?? Secrets["RabbitMq:Uri"]
                ?? throw new InvalidOperationException(
                    "Set BUILDINGBLOCKS_RABBITMQ_URI or the Tasks.Api user-secrets to run this test."
                )
        );

    public static string RedisConnectionString =>
        Environment.GetEnvironmentVariable("BUILDINGBLOCKS_REDIS")
        ?? Secrets.GetConnectionString("Redis")
        ?? "localhost:6379";

    /// <summary>La misma URI, pero pasando por el proxy.</summary>
    public static Uri ThroughProxy(Uri rabbit, TcpChaosProxy proxy) =>
        new UriBuilder(rabbit) { Host = "127.0.0.1", Port = proxy.Port }.Uri;

    /// <summary>La misma cadena de Redis (con su clave, si la tiene), pero pasando por el proxy.</summary>
    public static string ThroughProxy(string redis, TcpChaosProxy proxy)
    {
        var options = ConfigurationOptions.Parse(redis);
        options.EndPoints.Clear();
        options.EndPoints.Add("127.0.0.1", proxy.Port);
        return options.ToString(includePassword: true);
    }

    public static (string Host, int Port) RedisEndpoint(string redis)
    {
        var endpoint = ConfigurationOptions.Parse(redis).EndPoints[0];
        return endpoint switch
        {
            System.Net.DnsEndPoint dns => (dns.Host, dns.Port == 0 ? 6379 : dns.Port),
            System.Net.IPEndPoint ip => (ip.Address.ToString(), ip.Port == 0 ? 6379 : ip.Port),
            _ => ("localhost", 6379),
        };
    }

    public static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string because)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(because);
            await Task.Delay(200);
        }
    }

    public static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout, string because)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(because);
            await Task.Delay(500);
        }
    }
}
