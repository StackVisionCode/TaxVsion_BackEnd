using BuildingBlocks.Messaging.EmailIntegrationEvents;
using JasperFx.CodeGeneration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Postmaster.Application.Feedback.Commands.ProcessDeliveryFeedback;
using TaxVision.Postmaster.Application.Feedback.Commands.ReconcileDeliveryFeedback;
using Wolverine.Runtime.Handlers;
using Xunit;

namespace TaxVision.Postmaster.Tests.Integration;

/// <summary>
/// Los tests unitarios llaman al handler directo. Acá se arma con el contenedor real: si falta un
/// registro, revienta ahora y no con el primer aviso del proveedor.
/// </summary>
public sealed class DeliveryFeedbackHandlerCompilationTests(PostmasterApiFactory factory)
    : IClassFixture<PostmasterApiFactory>
{
    [Theory]
    [InlineData(typeof(ProcessDeliveryFeedbackCommand))]
    [InlineData(typeof(ReconcileDeliveryFeedbackCommand))]
    [InlineData(typeof(EmailProviderFeedbackReceivedIntegrationEvent))]
    [InlineData(
        typeof(BuildingBlocks.Messaging.ConnectorsIntegrationEvents.ConnectorsDeliveryReportDetectedIntegrationEvent)
    )]
    public void Feedback_handler_compiles_against_the_real_container(Type messageType)
    {
        var graph = factory.Services.GetServices<ICodeFileCollection>().OfType<HandlerGraph>().Single();

        Assert.NotNull(graph.HandlerFor(messageType));
    }
}
