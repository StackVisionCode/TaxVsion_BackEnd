using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;

namespace TaxVision.Auth.Infrastructure.Persistence;

/// <summary>
/// Factory de tiempo de diseño para dotnet-ef: evita levantar el host completo
/// (JWT/RabbitMQ/user-secrets) al crear o aplicar migraciones. La cadena de
/// conexión se toma de --connection, de la variable ConnectionStrings__Default,
/// o de un fallback local de desarrollo.
/// </summary>
public sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=TaxVision_Auth;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlServer(connectionString).Options;

        // dotnet-ef solo inspecciona el modelo (OnModelCreating) — nunca llama
        // SaveChangesAsync ni ejecuta una query real, así que ni el bus real de Wolverine
        // ni un ITenantContext funcional hacen falta aquí (HasQueryFilter solo arma la
        // expression tree en tiempo de modelado; nunca se evalúa HasTenant/TenantId).
        return new AuthDbContext(options, new DesignTimeOnlyMessageBus(), new DesignTimeOnlyTenantContext());
    }

    /// <summary>No-op: dotnet-ef nunca ejecuta una query, así que el filtro nunca evalúa estas propiedades.</summary>
    private sealed class DesignTimeOnlyTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    /// <summary>No-op: dotnet-ef nunca ejecuta SaveChangesAsync, así que ningún método debería dispararse en la práctica.</summary>
    private sealed class DesignTimeOnlyMessageBus : IMessageBus
    {
        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotSupportedException("Design-time only.");

        public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotSupportedException("Design-time only.");

        public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) =>
            throw new NotSupportedException("Design-time only.");

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message) =>
            throw new NotSupportedException("Design-time only.");

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
            throw new NotSupportedException("Design-time only.");

        public IDestinationEndpoint EndpointFor(string endpointName) =>
            throw new NotSupportedException("Design-time only.");

        public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotSupportedException("Design-time only.");

        public Task InvokeForTenantAsync(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException("Design-time only.");

        public Task<T> InvokeForTenantAsync<T>(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException("Design-time only.");

        public string? TenantId
        {
            get => null;
            set { }
        }

        public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
            throw new NotSupportedException("Design-time only.");

        public Task InvokeAsync(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException("Design-time only.");

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException("Design-time only.");

        public Task<T> InvokeAsync<T>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException("Design-time only.");

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            CancellationToken cancellation = default
        ) => throw new NotSupportedException("Design-time only.");

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default
        ) => throw new NotSupportedException("Design-time only.");
    }
}
