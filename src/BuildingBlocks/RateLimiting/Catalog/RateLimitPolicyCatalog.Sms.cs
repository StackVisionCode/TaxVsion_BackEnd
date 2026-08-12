namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    // Envío de SMS (POST /sms/messages, batch 1..N). Endpoint caro (cada request dispara envíos a un
    // proveedor externo con costo por mensaje), así que va como categoría H: partición (tenant, user)
    // + overlay por tenant + cap agregado por endpoint. SlidingWindow para suavizar ráfagas.
    // El fan-out real por mensaje/proveedor lo modelará la capa K en una fase posterior.
    public static readonly RateLimitPolicyDefinition SmsSend = Define(
        "sms.h.send",
        RateLimitCategory.H,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 30,
        windowSeconds: 60,
        RateLimitAlgorithm.SlidingWindow,
        overlayQuota: 300
    );
}
