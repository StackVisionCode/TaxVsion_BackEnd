using System.Collections.Concurrent;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Customer.Domain.Employees;
using TaxVision.Customer.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Tracking;
using Xunit;

namespace TaxVision.Customer.Tests.Integration;

/// <summary>
/// T-10 (P-17.5) contra SQL Server real: dos formularios distintos con el mismo email llegan a la vez.
/// El lock por oficina y email serializa las dos altas, así que la segunda ve a la primera en el
/// detector y responde <c>DuplicateFound</c> por email en vez de chocar contra el índice único.
/// Usa un tenant propio y borra lo que crea; los transportes externos van anulados.
/// </summary>
public sealed class CustomerCreationFromFormConcurrencyTests : IClassFixture<CustomerApiFactory>
{
    private const int Rounds = 10;
    private readonly WebApplicationFactory<Program> factory;

    public CustomerCreationFromFormConcurrencyTests(CustomerApiFactory baseFactory) =>
        factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.DisableAllExternalWolverineTransports();
                // Los jobs propios corren contra la base real: fuera mientras dura la prueba.
                foreach (
                    var job in services
                        .Where(descriptor =>
                            descriptor.ServiceType == typeof(IHostedService)
                            && descriptor.ImplementationType?.Namespace?.StartsWith(
                                "TaxVision.",
                                StringComparison.Ordinal
                            ) == true
                        )
                        .ToList()
                )
                    services.Remove(job);
            })
        );

    [Fact]
    public async Task Two_forms_with_the_same_email_in_parallel_create_one_customer_and_reject_the_other()
    {
        var tenantId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        await SeedCreatorAsync(tenantId, creatorId);
        var codec = factory.Services.GetRequiredService<IProtectedPayloadCodec>();
        var outcomes = new ConcurrentBag<string>();
        try
        {
            for (var round = 1; round <= Rounds; round++)
            {
                var email = $"t10-{round}-{tenantId:N}@example.test";
                var session = await factory
                    .Services.GetRequiredService<IHost>()
                    .TrackActivity()
                    .Timeout(TimeSpan.FromSeconds(60))
                    .ExecuteAndWaitAsync(
                        (IMessageContext _) =>
                            Task.WhenAll(
                                InvokeAsync(Command(tenantId, creatorId, email, codec)),
                                InvokeAsync(Command(tenantId, creatorId, email, codec))
                            )
                    );

                var completed = session.Sent.MessagesOf<CustomerCreationFromFormCompletedIntegrationEvent>().ToArray();
                var rejected = session.Sent.MessagesOf<CustomerCreationFromFormRejectedIntegrationEvent>().ToArray();
                Assert.Equal(1, await CountCustomersAsync(tenantId, email));
                var winner = Assert.Single(completed);
                var loser = Assert.Single(rejected);
                Assert.Equal(CustomerCreationFromFormOutcomes.DuplicateFound, loser.ErrorCode);
                Assert.Equal(winner.CustomerId, loser.ExistingCustomerId);
                Assert.Equal("Email", loser.MatchedBy);
                outcomes.Add($"{round}: completed + rejected(DuplicateFound, Email)");
            }
        }
        finally
        {
            await CleanUpAsync(tenantId);
        }

        Assert.Equal(Rounds, outcomes.Count);
    }

    private async Task InvokeAsync(CustomerCreationFromFormRequestedIntegrationEvent command)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMessageBus>().InvokeAsync(command);
    }

    private static CustomerCreationFromFormRequestedIntegrationEvent Command(
        Guid tenantId,
        Guid creatorId,
        string email,
        IProtectedPayloadCodec codec
    )
    {
        var sourceReferenceId = Guid.NewGuid();
        var participant = new ClientFormParticipant { Kind = "External", ParticipantId = Guid.NewGuid() };
        var idempotencyKey = $"t10:{sourceReferenceId:N}";
        var payload = new CustomerCreationFromFormPayload([
            new("customer.kind", 1, "Individual"),
            new("customer.person.first_name", 1, "Race"),
            new("customer.person.last_name", 1, $"T10 {sourceReferenceId:N}"[..12]),
            new("customer.primary_email", 1, email),
            new("customer.language", 1, "En"),
        ]);
        return new CustomerCreationFromFormRequestedIntegrationEvent
        {
            TenantId = tenantId,
            CorrelationId = "t10",
            RequestId = Guid.NewGuid(),
            IdempotencyKey = idempotencyKey,
            SourceService = CustomerCreationFromFormOutcomes.ClientFormsSourceService,
            SourceReferenceId = sourceReferenceId,
            RevisionNumber = 1,
            RequestedByUserId = creatorId,
            TargetCatalogVersion = 1,
            Participant = participant,
            Payload = codec.Protect(
                ClientFormProtectedPayloadSchemas.CustomerCreation,
                new ProtectedPayloadBinding(tenantId, sourceReferenceId, participant, IdempotencyKey: idempotencyKey),
                payload
            ),
        };
    }

    private async Task SeedCreatorAsync(Guid tenantId, Guid creatorId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
        db.TenantEmployeeDirectoryEntries.Add(
            TenantEmployeeDirectoryEntry.CreateReconciled(creatorId, tenantId, "TenantAdmin", true, false)
        );
        await db.SaveChangesAsync();
    }

    private async Task<int> CountCustomersAsync(Guid tenantId, string email)
    {
        await using var connection = new SqlConnection(ConnectionString());
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM Customers WHERE TenantId = @tenant AND PrimaryEmailNormalized = @email",
            connection
        );
        command.Parameters.AddWithValue("@tenant", tenantId);
        command.Parameters.AddWithValue("@email", email);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    // Solo filas del tenant de la prueba: cada tabla hija primero.
    private async Task CleanUpAsync(Guid tenantId)
    {
        await using var connection = new SqlConnection(ConnectionString());
        await connection.OpenAsync();
        foreach (
            var table in new[]
            {
                "CustomerPreparerAssignments",
                "CustomerAccessGrants",
                "Customers",
                "TenantEmployeeDirectoryEntries",
            }
        )
        {
            await using var command = new SqlCommand(
                $"IF OBJECT_ID('{table}') IS NOT NULL AND COL_LENGTH('{table}', 'TenantId') IS NOT NULL DELETE FROM {table} WHERE TenantId = @tenant",
                connection
            );
            command.Parameters.AddWithValue("@tenant", tenantId);
            await command.ExecuteNonQueryAsync();
        }
    }

    private string ConnectionString()
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Database.GetConnectionString()!;
    }
}
