namespace BuildingBlocks.ActorTypeAuthorization;

/// <summary>
/// Marca una acción como autorizada por un "capability token" (terminología de la Auth0 Tickets
/// API / el authorization code de OAuth RFC 6749 / OWASP "capability-based access control") en
/// vez de por una identidad persistente con <c>actor_type</c> — ej. el ticket firmado de un solo
/// uso que Auth emite para el self-registration de tenants (<c>reg_slug</c>/<c>reg_email</c>, sin
/// claim <c>actor_type</c> por diseño). <see cref="ActorTypeAuthorizationFilter"/> (Capa 2) se
/// salta por completo para acciones marcadas así — a diferencia de <c>[AllowAnonymous]</c>, este
/// atributo NO es <see cref="Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute"/> y por
/// lo tanto el middleware <c>UseAuthorization()</c> de ASP.NET Core no lo reconoce ni lo usa para
/// saltarse ningún <c>[Authorize(Policy = "...")]</c> existente (Capa 3) — esa policy sigue
/// aplicándose exactamente igual, sin cambios; solo se exime a la acción del chequeo adicional de
/// actor type, porque su propia policy dedicada ya autoriza correctamente al portador del token.
///
/// <para>
/// Úsalo ÚNICAMENTE cuando la acción ya tiene su propio mecanismo de autorización robusto y
/// específico (una policy con <c>RequireAssertion</c> sobre un claim real, validada por el
/// middleware de autorización de ASP.NET Core ANTES de que se ejecute cualquier filtro de MVC) y
/// el token legítimo para esa acción, por diseño, no lleva un <c>actor_type</c> persistente. No es
/// un atajo genérico para "no quiero anotar este endpoint" — cada uso debe documentar en el
/// controller/acción por qué el capability token no encaja en la taxonomía de <see cref="ActorType"/>.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class AuthorizedByCapabilityTokenAttribute : Attribute;
