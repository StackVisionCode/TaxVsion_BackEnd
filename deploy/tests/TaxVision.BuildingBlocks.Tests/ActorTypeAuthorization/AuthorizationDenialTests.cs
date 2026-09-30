using System.Text.Json;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.ActorTypeAuthorization;

/// <summary>
/// A5 del plan — el cuerpo del 403. Antes, un 403 de la capa de permisos o de la de actor type llegaba
/// **vacío**: el frontend no podía distinguir "no tenés el permiso" de "tu plan no incluye el módulo",
/// así que mostraba siempre la misma pantalla de acceso restringido. Estos tests fijan el contrato
/// RFC 9457 y, sobre todo, la compatibilidad: <c>code</c> y <c>message</c> siguen ahí para los
/// frontends ya desplegados.
/// </summary>
public sealed class AuthorizationDenialTests
{
    private static Dictionary<string, JsonElement> Serialize(AuthorizationDenial denial)
    {
        var problem = denial.ToProblemDetails(new DefaultHttpContext());
        var json = JsonSerializer.Serialize(problem, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
    }

    [Fact]
    public void A_permission_denial_carries_code_and_reason()
    {
        var body = Serialize(AuthorizationDenial.PermissionDenied);

        Assert.Equal("Authz.PermissionDenied", body["code"].GetString());
        Assert.Equal(AuthorizationDenialReasons.Permission, body["reason"].GetString());
        Assert.Equal(403, body["status"].GetInt32());
    }

    /// <summary>
    /// Lo que los frontends desplegados leen hoy es <c>{code, message}</c> (la forma de
    /// <see cref="Error"/>). RFC 9457 usa <c>detail</c>, así que el cuerpo lleva las dos: cambiar el
    /// contrato del 403 no puede romper al CRM ni al Portal que ya están en producción.
    /// </summary>
    [Fact]
    public void The_body_stays_readable_by_the_deployed_frontends()
    {
        var body = Serialize(AuthorizationDenial.PermissionDenied);

        Assert.Equal(body["detail"].GetString(), body["message"].GetString());
        Assert.False(string.IsNullOrWhiteSpace(body["message"].GetString()));
    }

    [Fact]
    public void A_module_denial_names_the_module_so_the_frontend_does_not_need_its_own_map()
    {
        var body = Serialize(AuthorizationDenial.ForModule("campaigns"));

        Assert.Equal("Authz.ModuleUnavailable", body["code"].GetString());
        Assert.Equal(AuthorizationDenialReasons.Module, body["reason"].GetString());
        Assert.Equal("campaigns", body["module"].GetString());
    }

    [Fact]
    public void A_permission_denial_can_name_the_permission_it_required()
    {
        var body = Serialize(AuthorizationDenial.ForPermission("customers.manage"));

        Assert.Equal("customers.manage", body["permission"].GetString());
        Assert.Equal(AuthorizationDenialReasons.Permission, body["reason"].GetString());
    }

    /// <summary>
    /// Un endpoint sin <c>[AllowActorTypes]</c> se bloquea igual (fail-closed), pero con un código
    /// propio: es un bug del backend, no una denegación legítima, y hay que poder alertarlo aparte.
    /// </summary>
    [Fact]
    public void A_missing_actor_type_declaration_is_distinguishable_from_a_real_denial()
    {
        Assert.NotEqual(AuthorizationDenial.ActorTypeNotAllowed.Code, AuthorizationDenial.ActorTypeNotDeclared.Code);
        Assert.Equal(
            AuthorizationDenialReasons.NotDeclared,
            Serialize(AuthorizationDenial.ActorTypeNotDeclared)["reason"].GetString()
        );
    }

    /// <summary>El código de la superficie no cambia: el CRM y el Portal ya lo leen.</summary>
    [Fact]
    public void The_surface_denial_keeps_its_existing_code()
    {
        var body = Serialize(AuthorizationDenial.SurfaceNotAllowed);

        Assert.Equal("Auth.SurfaceNotAllowed", body["code"].GetString());
        Assert.Equal(AuthorizationDenialReasons.Surface, body["reason"].GetString());
    }

    [Fact]
    public void A_denial_survives_the_round_trip_through_the_http_context()
    {
        var context = new DefaultHttpContext();
        Assert.Null(AuthorizationDenial.Recorded(context));

        AuthorizationDenial.ForModule("comms").Record(context);

        var recorded = AuthorizationDenial.Recorded(context);
        Assert.NotNull(recorded);
        Assert.Equal("comms", recorded!.Module);
    }

    [Fact]
    public void The_module_exception_carries_the_module_for_the_middleware()
    {
        var denial = AuthorizationDenial.ForModule("campaigns");
        var exception = new ModuleUnavailableException(denial.Code, denial.Detail, "campaigns");

        Assert.Equal("Authz.ModuleUnavailable", exception.Code);
        Assert.Equal("campaigns", exception.Module);
    }
}
