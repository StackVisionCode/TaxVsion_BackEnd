using BuildingBlocks.ActorTypeAuthorization;

namespace BuildingBlocks.Web.ActorTypeAuthorization;

/// <summary>
/// Abre un endpoint a tokens de otra superficie además del CRM y el portal (ver <see cref="AccessSurface"/>).
/// Sin este atributo, <see cref="SurfaceAuthorizationFilter"/> rechaza cualquier token con superficie.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class AllowSurfaceAttribute(params string[] surfaces) : Attribute
{
    public IReadOnlyCollection<string> Surfaces { get; } = surfaces;
}
