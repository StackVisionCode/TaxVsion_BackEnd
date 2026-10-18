using BuildingBlocks.Authorization.SlotGating;
using Microsoft.Data.SqlClient;

namespace BuildingBlocks.Infrastructure.SlotGating;

public sealed class SqlSeatSlotGate(string? connectionString) : ISeatSlotGate
{
    public async ValueTask<SeatSlotDecision> CheckAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:Auth is required for seat slot gating.");

        const string sql = """
            SELECT p.[IsGated], p.[SlotIndex], p.[EffectiveCap],
                   CASE WHEN u.[ActorType] = N'TenantAdmin'
                          AND u.[SlotPriorityAt] = CONVERT(datetime2, '0001-01-01T00:00:00.0000000')
                        THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS [IsOwner]
            FROM [TenantSeatSlotProjections] p
            INNER JOIN [Users] u ON u.[TenantId] = p.[TenantId] AND u.[Id] = p.[UserId]
            WHERE p.[TenantId] = @tenantId AND p.[UserId] = @userId;
            """;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tenantId", tenantId);
        command.Parameters.AddWithValue("@userId", userId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return new SeatSlotDecision(true, null, null, null);

        var isGated = reader.GetBoolean(0);
        var isOwner = reader.GetBoolean(3);
        return new SeatSlotDecision(
            Allowed: isOwner || !isGated,
            Reason: isGated && !isOwner ? "seats.user_slot_gated" : null,
            SlotIndex: reader.GetInt32(1),
            EffectiveCap: reader.GetInt32(2)
        );
    }
}
