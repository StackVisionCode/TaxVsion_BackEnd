namespace BuildingBlocks.Web.RateLimiting;

/// <summary>
/// Marca explícita de "este endpoint público queda sin política de rate-limit, y esta es la
/// razón" — invariante §3.10 del plan ("ningún endpoint queda sin categoría asignada... o
/// marcado con [RateLimitExempt(reason)] con justificación"). Fase 9 construye el NetArchTest
/// que exige uno de los dos (<see cref="RateLimitAttribute"/> o este) en todo <c>[HttpXxx]</c>
/// público. Los endpoints de <c>/health/*</c> no lo necesitan — no son acciones MVC, son
/// terminal middleware de <c>MapHealthChecks</c>, fuera del pipeline de filtros por completo.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class RateLimitExemptAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}
