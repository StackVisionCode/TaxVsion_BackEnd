using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Sms.Application.Messages.Commands;
using Wolverine;

namespace TaxVision.Sms.Api.Controllers;

/// <summary>Punto de entrada del diagrama: cualquier microservicio (M2M) o usuario autenticado envía
/// 1..N mensajes. El tenant se toma del JWT — nunca de un campo del caller.</summary>
[ApiController]
[Route("sms")]
[Authorize]
[AllowActorTypes(ActorType.Service, ActorType.TenantAdmin, ActorType.TenantEmployee)]
public sealed class MessagesController(
    IMessageBus bus,
    ITenantContext tenant,
    ICorrelationContext correlation,
    IUserPermissionsSource permissions
) : ControllerBase
{
    public sealed record MediaItemRequest(string Url, string ContentType, string? FileName, long? SizeBytes);

    public sealed record MessageItemRequest(
        Guid CustomerId,
        string To,
        string Message,
        IReadOnlyList<MediaItemRequest>? Media,
        string? IdempotencyKey,
        string? SourceContext,
        string? RecipientName
    );

    public sealed record SendMessagesRequest(IReadOnlyList<MessageItemRequest> Messages);

    private const string IdempotencyKeyHeader = "Idempotency-Key";

    // La clave por ítem ("crm:{clave}:{n}") cabe en la columna IdempotencyKey (200).
    private const int MaxRequestKeyLength = 100;

    [HttpPost("messages")]
    [HasPermission(SmsPermissions.Send)]
    [RateLimit("sms.h.send")]
    [ProducesResponseType<SendSmsBatchResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Send([FromBody] SendMessagesRequest request, CancellationToken ct)
    {
        // Clave de la petición: la del CRM (Idempotency-Key) o una nueva. Un reintento reusa lote y reserva;
        // un reenvío legítimo es otra petición y otra clave.
        var requestKey =
            Request.Headers.TryGetValue(IdempotencyKeyHeader, out var header) && !string.IsNullOrWhiteSpace(header)
                ? header.ToString().Trim()
                : Guid.NewGuid().ToString("N");
        if (requestKey.Length > MaxRequestKeyLength)
            return BadRequest(new Error("sms.invalidIdempotencyKey", "The Idempotency-Key header is too long."));

        var items = (request.Messages ?? [])
            .Select(
                (m, index) =>
                    new SmsSendItemDto(
                        m.CustomerId,
                        m.To,
                        m.Message,
                        m.Media?.Select(x => new SmsMediaDto(x.Url, x.ContentType, x.FileName, x.SizeBytes)).ToList(),
                        string.IsNullOrWhiteSpace(m.IdempotencyKey) ? $"crm:{requestKey}:{index}" : m.IdempotencyKey,
                        m.SourceContext,
                        m.RecipientName
                    )
            )
            .ToList();

        // El endpoint lo comparten los servicios (M2M) y las personas. Solo a una persona se le mide el
        // alcance: un servicio no tiene cartera de clientes, y la campaña que dispara el envío ya
        // resolvió la suya.
        Guid? actorUserId =
            User.GetActorType() == ActorType.Service || !User.TryGetUserId(out var userId) ? null : userId;
        var canViewAll =
            actorUserId is not null && await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);

        // Cobro money-OUT (F6): todo envío por ESTE endpoint es individual/directo → se cobra al Wallet. El
        // dispatch de campaña NO pasa por acá (usa CampaignSmsDispatchConsumer, que deja ChargeWallet=false).
        var result = await bus.InvokeAsync<Result<SendSmsBatchResponse>>(
            new SendSmsBatchCommand(
                tenant.TenantId,
                correlation.CorrelationId,
                items,
                actorUserId,
                canViewAll,
                ChargeWallet: true,
                CallerBearerToken: ExtractBearerToken(),
                BatchKey: requestKey,
                EnforceContactPolicy: true
            ),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>El bearer crudo de la petición actual, sin el prefijo "Bearer ". Null si no viene.</summary>
    private string? ExtractBearerToken()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header))
            return null;
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : header.Trim();
    }
}
