using BuildingBlocks.Results;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using Wolverine;

namespace TaxVision.Wallet.Application.Wallet.Commands;

/// <summary>Datos de <c>POST /wallet/top-ups</c>.</summary>
public sealed record TopUpCheckoutRequest(
    Guid TenantId,
    Guid RequestedByUserId,
    long AmountCents,
    string Currency,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string Provider = "Stripe",
    string Method = "Card"
);

/// <summary>
/// Inicia una recarga por <b>checkout hosteado</b> (00_Plan §6) sin retener una transacción durante la llamada a
/// PaymentApp (patrón BB-8): orden confirmada → sesión de pago → orden <c>Failed</c> si la sesión no sale. NO
/// acredita saldo: eso ocurre al confirmarse el pago (webhook → <c>WalletTopUpPaymentSucceeded</c>).
/// </summary>
public static class TopUpCheckoutFlow
{
    public const string CheckoutUnavailableReason = "checkout_unavailable";

    public static async Task<Result<TopUpCheckoutView>> RunAsync(
        IMessageBus bus,
        TopUpCheckoutRequest request,
        CancellationToken ct
    )
    {
        var order = await bus.InvokeAsync<Result<TopUpView>>(
            new CreateTopUpOrderCommand(
                request.TenantId,
                request.RequestedByUserId,
                request.AmountCents,
                request.Currency
            ),
            ct
        );
        if (order.IsFailure)
            return Result.Failure<TopUpCheckoutView>(order.Error);

        var start = new StartTopUpCheckoutCommand(
            request.TenantId,
            order.Value.Id,
            request.PayerEmail,
            request.SuccessUrl,
            request.CancelUrl,
            request.Provider,
            request.Method
        );

        Result<WalletTopUpCheckoutClientResult> session;
        try
        {
            session = await bus.InvokeAsync<Result<WalletTopUpCheckoutClientResult>>(start, ct);
        }
        catch (Exception)
        {
            await MarkFailedAsync(bus, request.TenantId, order.Value.Id);
            throw;
        }

        if (session.IsFailure)
        {
            await MarkFailedAsync(bus, request.TenantId, order.Value.Id);
            return Result.Failure<TopUpCheckoutView>(session.Error);
        }

        return Result.Success(
            new TopUpCheckoutView(order.Value, session.Value.CheckoutUrl, session.Value.ExpiresAtUtc)
        );
    }

    // Sin el token del pedido: un cliente que cortó la conexión tampoco deja la orden Pending.
    private static Task MarkFailedAsync(IMessageBus bus, Guid tenantId, Guid topUpId) =>
        bus.InvokeAsync<Result>(
            new MarkTopUpCheckoutFailedCommand(tenantId, topUpId, CheckoutUnavailableReason),
            CancellationToken.None
        );
}
