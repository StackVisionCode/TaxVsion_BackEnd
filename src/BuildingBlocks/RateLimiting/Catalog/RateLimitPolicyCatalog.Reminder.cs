namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    // Compartida por GET /reminders y GET /reminders/{id} — mismo perfil de lectura simple.
    public static readonly RateLimitPolicyDefinition ReminderRead = Define(
        "reminder.f.read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition ReminderCreate = Define(
        "reminder.g.create",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Compartida por UpdateSchedule/UpdateSubject/Snooze/Dismiss — escrituras simples sobre un
    // recordatorio existente. Snooze y Dismiss son los dos que más se disparan desde el frontend
    // (un click por notificación), de ahí que compartan cuota con el resto de la escritura.
    public static readonly RateLimitPolicyDefinition ReminderUpdate = Define(
        "reminder.g.update",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    public static readonly RateLimitPolicyDefinition ReminderDelete = Define(
        "reminder.g.delete",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // GET /reminders/upcoming — barre un rango de FireAtUtc sobre el índice
    // (TenantId, UserId, Status, FireAtUtc), más caro que leer uno por Id.
    public static readonly RateLimitPolicyDefinition ReminderUpcoming = Define(
        "reminder.h.upcoming",
        RateLimitCategory.H,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 20,
        windowSeconds: 60,
        RateLimitAlgorithm.SlidingWindow,
        overlayQuota: 100
    );
}
