using Wolverine;
using Wolverine.RabbitMQ;
using Wolverine.RabbitMQ.Internal;

namespace BuildingBlocks.Web.Hosting;

/// <summary>
/// Transporte RabbitMQ común a los servicios .NET, con las confirmaciones de publicación activadas.
///
/// <para>
/// Wolverine 6.14 abre los canales sin confirmaciones por defecto: <c>BasicPublishAsync</c> vuelve en
/// cuanto el mensaje sale por el socket y el outbox durable borra la fila como enviada. Si el broker
/// rechaza la publicación después (canal cerrado por permisos, exchange inexistente), el mensaje se
/// pierde sin reintento. Medido en T-2 de Client Forms (2026-10-23): `Completed` y `CustomerCreated`
/// desaparecieron del outbox con el canal cerrado por un 403.
/// </para>
///
/// <para>
/// Con las confirmaciones rastreadas, la publicación espera el ack del broker y un rechazo llega como
/// excepción: el outbox conserva la fila y Wolverine reintenta.
/// </para>
///
/// <para>Uso: <c>options.UseTaxVisionRabbitMq(rabbitUri);</c> en lugar de <c>UseRabbitMq(...).AutoProvision()</c>.</para>
/// </summary>
public static class RabbitMqTransportExtensions
{
    public static RabbitMqTransportExpression UseTaxVisionRabbitMq(this WolverineOptions options, Uri rabbitUri)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(rabbitUri);

        return options
            .UseRabbitMq(rabbitUri)
            .AutoProvision()
            .ConfigureChannelCreation(channel =>
            {
                channel.PublisherConfirmationsEnabled = true;
                channel.PublisherConfirmationTrackingEnabled = true;
            });
    }

    public static RabbitMqTransportExpression UseTaxVisionRabbitMq(this WolverineOptions options, string rabbitUri) =>
        options.UseTaxVisionRabbitMq(new Uri(rabbitUri));
}
