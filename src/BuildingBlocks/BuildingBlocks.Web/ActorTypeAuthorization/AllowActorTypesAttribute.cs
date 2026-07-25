namespace BuildingBlocks.ActorTypeAuthorization;

/// <summary>
/// Declara para qué <see cref="ActorType"/>(s) es un endpoint — Capa 2 del plan de autorización
/// por actor type (ver Actor_Type_Authorization_Layers_Plan.md, sección 5). La lee
/// <see cref="ActorTypeAuthorizationFilter"/>, registrado globalmente para toda acción de todos
/// los controllers: si una acción no tiene este atributo (ni en el método ni en el controller),
/// el filtro la bloquea por default (fail-closed) — nunca se abre por default. Puede declararse
/// a nivel de controller (aplica a todas sus acciones) o a nivel de método (sobreescribe al del
/// controller para esa acción puntual).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class AllowActorTypesAttribute(params ActorType[] actorTypes) : Attribute
{
    public IReadOnlyCollection<ActorType> ActorTypes { get; } = actorTypes;
}
