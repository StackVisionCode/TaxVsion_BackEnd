using BuildingBlocks.Messaging.EmailIntegrationEvents;
using JasperFx.CodeGeneration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Runtime.Handlers;
using Xunit;

namespace TaxVision.Postmaster.Tests.Integration;

/// <summary>
/// El consumer cuenta las entregas con el <c>Envelope</c> que inyecta Wolverine para saber cuándo dejar
/// de diferir un email por encima del cupo. Los tests unitarios llaman a <c>Handle</c> directo y no ven
/// si el código generado realmente se lo pasa: acá se genera el código del handler contra el host real.
/// </summary>
public sealed class EmailConsumerHandlerCompilationTests(PostmasterApiFactory factory)
    : IClassFixture<PostmasterApiFactory>
{
    [Fact]
    public void Wolverine_passes_the_current_delivery_envelope_to_the_email_consumer()
    {
        var graph = factory.Services.GetServices<ICodeFileCollection>().OfType<HandlerGraph>().Single();
        // Compila el handler como en runtime (con el contenedor real) y lee el fuente que generó.
        Assert.NotNull(graph.HandlerFor(typeof(NotificationsEmailSendRequestedIntegrationEvent)));
        var chain = graph.ChainFor(typeof(NotificationsEmailSendRequestedIntegrationEvent));
        Assert.NotNull(chain);
        var code = GeneratedSourceOf(chain);

        var call = code.Split('\n').Single(line => line.Contains("NotificationsEmailSendRequestedConsumer.Handle("));
        Assert.Contains("cancellation, context.Envelope)", call);
    }

    /// <summary>Wolverine no expone el fuente generado de un chain: vive en un campo <see cref="GeneratedType"/>.</summary>
    private static string GeneratedSourceOf(object chain)
    {
        for (var type = chain.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetFields(
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.DeclaredOnly
                )
                .FirstOrDefault(f => f.FieldType == typeof(GeneratedType));
            if (field?.GetValue(chain) is GeneratedType generated && generated.SourceCode is { } source)
                return source;
        }

        throw new InvalidOperationException($"No generated source found on {chain.GetType().Name}.");
    }
}
