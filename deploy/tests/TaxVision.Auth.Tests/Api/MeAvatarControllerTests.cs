using System.Reflection;
using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Auth.Api.Controllers;
using TaxVision.Auth.Application.Users.Avatars;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Api;

/// <summary>
/// <c>/auth/me/avatar</c>: solo staff (el cliente del portal tiene su foto en Customer), identidad del
/// token, tope de tamaño antes de leer el archivo y 202 al aceptar la subida.
/// </summary>
public sealed class MeAvatarControllerTests
{
    [Theory]
    [InlineData(nameof(MeAvatarController.Upload))]
    [InlineData(nameof(MeAvatarController.Remove))]
    public void CustomerPortal_recibe_403_del_filtro_de_actor_type(string action)
    {
        var context = FilterContext(action, ActorType.CustomerPortal);

        new ActorTypeAuthorizationFilter().OnAuthorization(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    [Theory]
    [InlineData(ActorType.TenantEmployee)]
    [InlineData(ActorType.TenantAdmin)]
    [InlineData(ActorType.PlatformAdmin)]
    public void El_staff_pasa_el_filtro_de_actor_type(ActorType actorType)
    {
        var context = FilterContext(nameof(MeAvatarController.Upload), actorType);

        new ActorTypeAuthorizationFilter().OnAuthorization(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public async Task Upload_arma_el_comando_desde_el_token_y_responde_202()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var bus = new FakeMessageBus
        {
            InvokeHandler = _ => Result.Success(new UploadMyAvatarResponse(fileId, "processing")),
        };

        var response = await ControllerFor(bus, tenantId, userId).Upload(FormFile([1, 2, 3]), CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(response);
        Assert.Equal(fileId, Assert.IsType<UploadMyAvatarResponse>(accepted.Value).FileId);
        var command = Assert.IsType<UploadMyAvatarCommand>(Assert.Single(bus.Invoked));
        Assert.Equal(tenantId, command.TenantId);
        Assert.Equal(userId, command.UserId);
        Assert.Equal([1, 2, 3], command.Content);
    }

    [Fact]
    public async Task Upload_sin_archivo_es_400_sin_tocar_el_bus()
    {
        var bus = new FakeMessageBus();

        var response = await ControllerFor(bus, Guid.NewGuid(), Guid.NewGuid()).Upload(null, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(response).StatusCode);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task Upload_de_mas_de_5MB_es_413_sin_leer_el_archivo()
    {
        var bus = new FakeMessageBus();
        var huge = new FormFile(Stream.Null, 0, 5 * 1024 * 1024 + 1, "file", "big.jpg");

        var response = await ControllerFor(bus, Guid.NewGuid(), Guid.NewGuid()).Upload(huge, CancellationToken.None);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, Assert.IsType<ObjectResult>(response).StatusCode);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task Upload_mapea_el_error_del_handler()
    {
        var bus = new FakeMessageBus
        {
            InvokeHandler = _ =>
                Result.Failure<UploadMyAvatarResponse>(new Error("Avatar.UnsupportedFormat", "Only JPEG...")),
        };

        var response = await ControllerFor(bus, Guid.NewGuid(), Guid.NewGuid())
            .Upload(FormFile([1]), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(response).StatusCode);
    }

    [Fact]
    public async Task Remove_responde_204()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var bus = new FakeMessageBus { InvokeHandler = _ => Result.Success() };

        var response = await ControllerFor(bus, tenantId, userId).Remove(CancellationToken.None);

        Assert.IsType<NoContentResult>(response);
        var command = Assert.IsType<RemoveMyAvatarCommand>(Assert.Single(bus.Invoked));
        Assert.Equal(userId, command.UserId);
        Assert.Equal(tenantId, command.TenantId);
    }

    [Fact]
    public async Task Sin_claims_es_401()
    {
        var bus = new FakeMessageBus();
        var controller = new MeAvatarController(bus)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        Assert.IsType<UnauthorizedResult>(await controller.Remove(CancellationToken.None));
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public void El_tope_del_cuerpo_cubre_5MB_mas_el_sobre_multipart()
    {
        var limit = typeof(MeAvatarController)
            .GetMethod(nameof(MeAvatarController.Upload))!
            .GetCustomAttribute<RequestSizeLimitAttribute>();

        Assert.NotNull(limit);
        Assert.True(
            MeAvatarController.MaxRequestBytes > BuildingBlocks.Imaging.AvatarImageOptions.DefaultMaxInputBytes
        );
        // El Gateway acepta hasta 6 MiB en /auth: el tope del servicio queda por debajo.
        Assert.True(MeAvatarController.MaxRequestBytes <= 6_291_456);
    }

    private static MeAvatarController ControllerFor(FakeMessageBus bus, Guid tenantId, Guid userId)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimNames.TenantId, tenantId.ToString()), new Claim("sub", userId.ToString())],
            authenticationType: "test"
        );
        return new MeAvatarController(bus)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
    }

    private static FormFile FormFile(byte[] bytes) =>
        new(new MemoryStream(bytes), 0, bytes.Length, "file", "photo.jpg");

    private static AuthorizationFilterContext FilterContext(string action, ActorType actorType)
    {
        var descriptor = new ControllerActionDescriptor
        {
            MethodInfo = typeof(MeAvatarController).GetMethod(action)!,
            ControllerTypeInfo = typeof(MeAvatarController).GetTypeInfo(),
        };
        var services = new ServiceCollection().AddSingleton<AuthorizationMetrics>().BuildServiceProvider();
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimNames.ActorType, actorType.ToString())], authenticationType: "test")
        );
        var httpContext = new DefaultHttpContext { User = principal, RequestServices = services };
        return new AuthorizationFilterContext(new ActionContext(httpContext, new RouteData(), descriptor), []);
    }
}
