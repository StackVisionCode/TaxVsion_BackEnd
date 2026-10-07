using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Wallet.Application.Pricing;
using TaxVision.Wallet.Application.Reservations;
using TaxVision.Wallet.Application.Reservations.Commands;
using Wolverine;

namespace TaxVision.Wallet.Api.Controllers;

/// <summary>
/// Endpoints <b>internos</b> cross-service del monedero (NO expuestos en el Gateway público; se llaman
/// contenedor-a-contenedor). Es el PEP money-OUT (00_Plan §5): reservar fondos antes de gastar y liquidarlos
/// después. El Wallet es <b>independiente</b> del consumidor — trabaja sobre una referencia opaca
/// (<c>referenceType</c> + <c>referenceId</c>), así que lo usa Campaigns, los envíos individuales, o cualquier
/// servicio futuro.
///
/// <para>Actores: miembros del tenant (on-behalf-of — el consumidor reenvía el JWT del usuario) o
/// <see cref="ActorType.Service"/> (M2M, p.ej. liquidación disparada por un evento sin sesión humana). NO lleva
/// <c>[HasPermission]</c>: el gasto ya está autorizado aguas arriba por el consumidor; este endpoint es el
/// enforcement del saldo, no un control de acceso de usuario.</para>
/// </summary>
[ApiController]
[Route("internal/wallet")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin, ActorType.Service)]
public sealed class InternalWalletController(IMessageBus bus) : ControllerBase
{
    /// <summary>Reserva fondos para una referencia: unidades por canal a cobrar + la referencia que la respalda.</summary>
    public sealed record ReserveRequest(
        string ReferenceType,
        Guid ReferenceId,
        long Email,
        long Sms,
        long Push,
        long WhatsApp
    );

    /// <summary>Liquida la reserva de una referencia: cuántas de las unidades reservadas se usaron de verdad.</summary>
    public sealed record SettleRequest(string ReferenceType, Guid ReferenceId, int ConsumedUnits);

    /// <summary>
    /// Reserva (cotiza + aparta) fondos. Siempre 200 — <see cref="ReservationView.Authorized"/> dice si pasó;
    /// si no, <see cref="ReservationView.DeficitMicros"/> lleva el faltante. Idempotente por referencia.
    /// </summary>
    [HttpPost("reservations")]
    [RateLimit("wallet.g.authorize")]
    [ProducesResponseType<ReservationView>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reserve(ReserveRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var units = new PerChannelUnits(request.Email, request.Sms, request.Push, request.WhatsApp);
        var result = await bus.InvokeAsync<Result<ReservationView>>(
            new ReserveFundsCommand(tenantId, request.ReferenceType, request.ReferenceId, units),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Liquida la reserva: consume lo usado y libera el resto. Idempotente.</summary>
    [HttpPost("reservations/settle")]
    [RateLimit("wallet.g.authorize")]
    [ProducesResponseType<SettlementView>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Settle(SettleRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<SettlementView>>(
            new SettleReservationCommand(tenantId, request.ReferenceType, request.ReferenceId, request.ConsumedUnits),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
