using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetArchTest.Rules;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Tests.Architecture;

/// <summary>
/// Fase 12 (plan de hardening §4) — fitness tests de las fronteras de Clean Architecture, mismo
/// criterio que <c>CorrespondenceArchitectureTests</c>/<c>ConnectorsArchitectureTests</c>/
/// <c>ScribeArchitectureTests</c>: Domain no depende de nada de afuera, Application no depende de
/// Infrastructure/Api, Infrastructure no depende de Api, controllers no le hablan a Infrastructure
/// directo. Postmaster era, hasta esta fase, el único de los 4 microservicios de este esfuerzo sin
/// ninguna red de seguridad arquitectónica automatizada (confirmado por dos auditorías
/// independientes).
///
/// Postmaster tiene una particularidad que ninguno de los 3 hermanos tiene en la misma medida:
/// CUATRO clientes salientes M2M reales (a diferencia de Connectors/Scribe, que tienen un patrón
/// único) — <c>PostmasterServiceTokenAcquirer</c>, <c>CloudStorageInlineAssetFetcher</c> y
/// <c>CloudStorageOutboundAttachmentFetcher</c> (namespace <c>Providers.Assets</c>), y
/// <c>ConnectorsSendClient</c> (namespace <c>Providers.Connectors</c>) — confirmados por el propio
/// plan de hardening (Fase 13) y por grep exhaustivo de <c>System.Net.Http</c>/<c>HttpClient</c>
/// sobre todo <c>TaxVision.Postmaster.Infrastructure</c>: solo esos 4 archivos más
/// <c>DependencyInjection.cs</c> (la composition root, que los registra vía
/// <c>AddHttpClient&lt;TInterface, TImplementation&gt;</c>) tocan HTTP saliente. El test de abajo
/// (<see cref="Only_the_designated_outbound_clients_should_reference_http_client"/>) convierte esa
/// observación en una regla — dado que Postmaster es, por diseño, el único punto de salida tanto
/// para notificaciones automáticas como para correspondencia humana (riesgo de "concentración de
/// responsabilidad" documentado en la Fase 15 de este mismo plan), es directamente valioso que
/// ningún handler de Application ni ningún otro tipo de Infrastructure pueda agregar un quinto
/// cliente HTTP disperso sin que este test lo note.
/// </summary>
public sealed class PostmasterArchitectureTests
{
    private static readonly System.Reflection.Assembly DomainAssembly = typeof(SentMessage).Assembly;
    private static readonly System.Reflection.Assembly ApplicationAssembly =
        typeof(TaxVision.Postmaster.Application.Sending.Commands.SendCorrespondenceMessage.SendCorrespondenceMessageCommand).Assembly;
    private static readonly System.Reflection.Assembly InfrastructureAssembly =
        typeof(TaxVision.Postmaster.Infrastructure.Persistence.PostmasterDbContext).Assembly;
    private static readonly System.Reflection.Assembly ApiAssembly =
        typeof(TaxVision.Postmaster.Api.Controllers.CorrespondenceMessagesController).Assembly;

    [Fact]
    public void Domain_should_not_depend_on_application()
    {
        var result = Types
            .InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("TaxVision.Postmaster.Application")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Domain_should_not_depend_on_infrastructure()
    {
        var result = Types
            .InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("TaxVision.Postmaster.Infrastructure")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Domain_should_not_depend_on_api()
    {
        var result = Types
            .InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("TaxVision.Postmaster.Api")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Domain_should_not_depend_on_entity_framework()
    {
        var result = Types
            .InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Domain_should_not_depend_on_wolverine()
    {
        var result = Types.InAssembly(DomainAssembly).Should().NotHaveDependencyOn("Wolverine").GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Domain_should_not_depend_on_http_client()
    {
        var result = Types.InAssembly(DomainAssembly).Should().NotHaveDependencyOn("System.Net.Http").GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    /// <summary>
    /// Postmaster.Infrastructure sí usa MailKit (<c>Providers/Smtp/SmtpEmailSender.cs</c>) para armar
    /// y enviar el MIME final — pero el dominio (<c>SentMessage</c>/<c>SentMessageRecipient</c>/
    /// <c>InlineAsset</c>/<c>OutboundAttachmentRef</c>) solo modela datos y reglas de negocio, nunca
    /// la librería de envío en sí. Mismo criterio que <c>CorrespondenceArchitectureTests</c>.
    /// </summary>
    [Fact]
    public void Domain_should_not_depend_on_mailkit()
    {
        var result = Types.InAssembly(DomainAssembly).Should().NotHaveDependencyOn("MailKit").GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Application_should_not_depend_on_infrastructure()
    {
        var result = Types
            .InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOn("TaxVision.Postmaster.Infrastructure")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Application_should_not_depend_on_api()
    {
        var result = Types
            .InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOn("TaxVision.Postmaster.Api")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Infrastructure_should_not_depend_on_api()
    {
        var result = Types
            .InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOn("TaxVision.Postmaster.Api")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    /// <summary>
    /// Mismo criterio que <c>CorrespondenceArchitectureTests.Controllers_should_not_depend_on_infrastructure</c>
    /// (agregado a Correspondence en la Fase 2 de este mismo esfuerzo de hardening) — los controllers
    /// deben orquestar solo a través de Application (comandos/queries/<c>IMessageBus</c> de
    /// Wolverine), nunca hablarle a Infrastructure directo. Hoy ningún controller de Postmaster viola
    /// esto (<c>MessagesController</c>/<c>CorrespondenceMessagesController</c>/
    /// <c>SuppressionController</c>/<c>ProvidersController</c> solo usan tipos de Application), pero
    /// nada lo protegía a futuro hasta este test.
    /// </summary>
    [Fact]
    public void Controllers_should_not_depend_on_infrastructure()
    {
        var result = Types
            .InAssembly(ApiAssembly)
            .That()
            .ResideInNamespace("TaxVision.Postmaster.Api.Controllers")
            .Should()
            .NotHaveDependencyOn("TaxVision.Postmaster.Infrastructure")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    /// <summary>
    /// Solo los 4 clientes salientes M2M designados —
    /// <c>TaxVision.Postmaster.Infrastructure.Providers.Assets.PostmasterServiceTokenAcquirer</c>,
    /// <c>...Providers.Assets.CloudStorageInlineAssetFetcher</c>,
    /// <c>...Providers.Assets.CloudStorageOutboundAttachmentFetcher</c> y
    /// <c>...Providers.Connectors.ConnectorsSendClient</c> (confirmados por la Fase 13 de este mismo
    /// plan y por grep exhaustivo de <c>System.Net.Http</c>/<c>HttpClient</c> sobre todo el ensamblado
    /// de Infrastructure) — pueden depender de <c>System.Net.Http</c>. <c>DependencyInjection.cs</c>
    /// (composition root) queda excluido a propósito, igual que en
    /// <c>CorrespondenceArchitectureTests.Only_PostmasterClient_should_reference_the_concrete_postmaster_http_client</c>,
    /// porque registrar los <c>HttpClient</c> tipados vía <c>AddHttpClient&lt;TInterface,
    /// TImplementation&gt;</c> es su trabajo. Deliberadamente NO se excluye el resto de
    /// <c>Providers</c> (<c>ProviderResolver</c>, <c>Providers/Smtp/SmtpEmailSender</c>,
    /// <c>Providers/Connectors/OAuthProviderResolver</c>) — ninguno de ellos necesita HTTP saliente
    /// hoy (SMTP usa MailKit, no <c>HttpClient</c>), así que quedan protegidos por esta misma regla en
    /// vez de heredar una excepción de namespace más amplia de la necesaria.
    ///
    /// Este test es el que le da valor real al hallazgo de "concentración de responsabilidad" de la
    /// Fase 15: con la sincronía (M2M) y la asincronía (consumers de eventos) conviviendo en el mismo
    /// servicio, es fácil que un handler de Application termine llamando HTTP directo "por
    /// conveniencia" en vez de pasar por la abstracción ya designada — este test lo evita antes de que
    /// pase, no solo lo documenta. (<c>Application_should_not_depend_on_infrastructure</c> ya bloquea
    /// que Application referencie estos tipos concretos; esta regla cubre además el caso de que un
    /// tipo nuevo dentro de la propia Infrastructure, fuera de los 4 designados, empiece a construir
    /// su propio <c>HttpClient</c>.)
    /// </summary>
    [Fact]
    public void Only_the_designated_outbound_clients_should_reference_http_client()
    {
        var result = Types
            .InAssembly(InfrastructureAssembly)
            .That()
            .DoNotResideInNamespace("TaxVision.Postmaster.Infrastructure.Providers.Assets")
            .And()
            .DoNotResideInNamespace("TaxVision.Postmaster.Infrastructure.Providers.Connectors")
            .And()
            .DoNotHaveName(["DependencyInjection"])
            .Should()
            .NotHaveDependencyOn("System.Net.Http")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    /// <summary>
    /// Postmaster nunca renderiza — recibe el HTML/inline-assets ya resueltos por Scribe vía el
    /// evento <c>NotificationsEmailSendRequestedIntegrationEvent</c> (decisión de wire-format
    /// "referencia, no bytes" de la Fase 9 del plan de hardening, ver comentarios de
    /// <c>RenderedContent</c>/<c>NotificationsEmailSendRequestedConsumer</c>) — solo resuelve bytes
    /// reales contra CloudStorage justo antes de armar el MIME. Este test CONFIRMA esa premisa en vez
    /// de asumirla, mismo criterio que
    /// <c>CorrespondenceArchitectureTests.No_type_should_reference_scribe</c>: las únicas menciones a
    /// "Scribe" en todo Postmaster son comentarios XML (<c>&lt;c&gt;Scribe&lt;/c&gt;</c>), nunca una
    /// dependencia de ensamblado real — no existe ningún <c>ProjectReference</c> a un proyecto de
    /// Scribe en ningún <c>.csproj</c> de Postmaster.
    /// </summary>
    [Fact]
    public void No_type_should_reference_scribe()
    {
        foreach (var assembly in new[] { DomainAssembly, ApplicationAssembly, InfrastructureAssembly, ApiAssembly })
        {
            var result = Types.InAssembly(assembly).Should().NotHaveDependencyOn("TaxVision.Scribe").GetResult();
            Assert.True(result.IsSuccessful, Describe(result));
        }
    }

    /// <summary>
    /// Fase 6 del plan de autorización por actor type — fitness function que falla el build si un
    /// controller nuevo (o una acción nueva) queda sin <see cref="AllowActorTypesAttribute"/>, sea a
    /// nivel de acción o heredado del controller. Espeja exactamente la resolución que hace
    /// <c>ActorTypeAuthorizationFilter.ResolveDeclaredActorTypes</c> en runtime (método primero,
    /// controller como fallback) y respeta las mismas dos excepciones que el filtro
    /// (<see cref="AllowAnonymousAttribute"/>, <see cref="AuthorizedByCapabilityTokenAttribute"/>).
    /// </summary>
    [Fact]
    public void Controller_actions_should_declare_AllowActorTypes()
    {
        var violations = FindActionsMissingAllowActorTypes(ApiAssembly);
        Assert.True(
            violations.Count == 0,
            "Actions missing [AllowActorTypes] (method or controller level): " + string.Join(", ", violations)
        );
    }

    private static List<string> FindActionsMissingAllowActorTypes(Assembly apiAssembly)
    {
        var controllerTypes = Types
            .InAssembly(apiAssembly)
            .That()
            .Inherit(typeof(ControllerBase))
            .And()
            .AreClasses()
            .GetTypes();

        var violations = new List<string>();
        foreach (var controllerType in controllerTypes)
        {
            var classIsAnonymous =
                controllerType.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null;
            var classIsCapabilityToken =
                controllerType.GetCustomAttribute<AuthorizedByCapabilityTokenAttribute>(inherit: true) is not null;
            var classAllowActorTypes = controllerType.GetCustomAttribute<AllowActorTypesAttribute>(inherit: true);

            var actions = controllerType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName && method.GetCustomAttribute<NonActionAttribute>() is null);

            foreach (var action in actions)
            {
                if (classIsAnonymous || action.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
                    continue;
                if (
                    classIsCapabilityToken
                    || action.GetCustomAttribute<AuthorizedByCapabilityTokenAttribute>() is not null
                )
                    continue;

                var allowActorTypes = action.GetCustomAttribute<AllowActorTypesAttribute>() ?? classAllowActorTypes;
                if (allowActorTypes is null)
                    violations.Add($"{controllerType.FullName}.{action.Name}");
            }
        }

        return violations;
    }

    private static string Describe(TestResult result) =>
        result.FailingTypes is null
            ? "Unknown architecture violation."
            : "Violating types: " + string.Join(", ", result.FailingTypes.Select(t => t.FullName));
}
