using System.Reflection;
using BuildingBlocks.Messaging;
using TaxVision.Auth.Application.SlotGating;
using TaxVision.Auth.Application.SlotGating.Consumers;

namespace TaxVision.Auth.Tests.SlotGating;

/// <summary>
/// Fitness obligatorios del plan §6: owner nunca gated, cobertura de controllers detrás del gate, y
/// consumers de recompute siempre con TenantId en el payload.
/// </summary>
public sealed class SeatSlotGatingFitnessTests
{
    [Fact]
    public void Owner_is_never_gated_across_random_tenants()
    {
        // Property-based con seed fijo para reproducir el run exacto si falla.
        var random = new Random(20261017);
        var tenantId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;

        for (var iteration = 0; iteration < 100; iteration++)
        {
            var userCount = random.Next(0, 50);
            var cap = random.Next(0, 100);
            var candidates = new List<TenantSeatSlotCandidate>(userCount + 1);

            // Un owner obligatorio: el calculator debe nunca gatearlo.
            var ownerId = Guid.NewGuid();
            candidates.Add(new TenantSeatSlotCandidate(ownerId, IsOwner: true, SlotPriorityAt: DateTime.MinValue));

            for (var i = 0; i < userCount; i++)
            {
                var hasPriority = random.NextDouble() > 0.1;
                var priority = hasPriority ? nowUtc.AddMinutes(-random.Next(0, 100_000)) : (DateTime?)null;
                candidates.Add(new TenantSeatSlotCandidate(Guid.NewGuid(), IsOwner: false, priority));
            }

            var projection = TenantSeatSlotCalculator.Calculate(tenantId, candidates, cap, nowUtc, revisionNumber: 1);

            var ownerRow = projection.Single(row => row.UserId == ownerId);
            Assert.False(
                ownerRow.IsGated,
                $"Owner gated at iteration {iteration} with cap={cap} users={userCount} slot={ownerRow.SlotIndex}"
            );
        }
    }

    [Fact]
    public void Gateway_pipeline_registers_seat_slot_gate_middleware()
    {
        // El plan garantiza que ningún -api se expone al host — el único entry point público es el
        // Gateway (ver DockerComposeNoPublicApiPortsFitnessTests). Para que el gate cubra todo
        // controller con [Authorize] de los microservicios, basta con que el middleware esté
        // registrado en el pipeline del Gateway: cualquier request llega acá antes del YARP.
        var gatewayProgram = ReadRepoFile("src/Gateway/TaxVision.Gateway/Program.cs");
        Assert.Contains("app.UseMiddleware<SeatSlotGateMiddleware>()", gatewayProgram);

        // Y debe estar entre UseAuthentication (necesita el JWT ya validado) y UseAuthorization.
        var authnIndex = gatewayProgram.IndexOf("app.UseAuthentication()", StringComparison.Ordinal);
        var gateIndex = gatewayProgram.IndexOf("app.UseMiddleware<SeatSlotGateMiddleware>()", StringComparison.Ordinal);
        var authzIndex = gatewayProgram.IndexOf("app.UseAuthorization()", StringComparison.Ordinal);
        Assert.True(authnIndex > 0 && gateIndex > authnIndex);
        Assert.True(
            authzIndex > gateIndex,
            "SeatSlotGateMiddleware must sit between UseAuthentication and UseAuthorization."
        );
    }

    [Fact]
    public void Every_event_consumed_by_recompute_consumer_carries_a_tenant_id()
    {
        // Un consumer de recompute sin tenant_id no sabría a quién tocar — se garantiza con
        // reflection sobre las handler methods del consumer.
        var consumerType = typeof(RecomputeTenantSeatSlotsConsumer);
        var handlerMethods = consumerType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == "Handle")
            .ToArray();

        Assert.NotEmpty(handlerMethods);

        var offenders = new List<string>();
        foreach (var method in handlerMethods)
        {
            var eventType = method.GetParameters()[0].ParameterType;
            // Todos los eventos del bus deben implementar IIntegrationEvent (TenantId: Guid no nullable).
            if (!typeof(IIntegrationEvent).IsAssignableFrom(eventType))
            {
                offenders.Add($"{eventType.Name} no implementa IIntegrationEvent");
                continue;
            }

            var tenantIdProp = eventType.GetProperty("TenantId");
            if (tenantIdProp is null)
                offenders.Add($"{eventType.Name} no expone TenantId");
            else if (tenantIdProp.PropertyType != typeof(Guid))
                offenders.Add($"{eventType.Name}.TenantId no es Guid (es {tenantIdProp.PropertyType.Name})");
        }

        Assert.True(offenders.Count == 0, string.Join("; ", offenders));
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate '{relativePath}' walking up from {AppContext.BaseDirectory}."
        );
    }
}
