using System.Text.RegularExpressions;
using BuildingBlocks.Infrastructure.RateLimit;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.RateLimit;

/// <summary>
/// RateLimit Fase 9 (Plan_Implementacion_Fases.md §8) — fitness functions de cierre del plan de 9
/// fases. Cubre los 3 invariantes que le tocan a <c>BuildingBlocks.Tests</c> (el (a) — todo endpoint
/// público con <c>[RateLimit]</c>/<c>[RateLimitExempt]</c> — vive distribuido, un test por servicio,
/// mismo patrón que las fitness functions de <c>AllowActorTypesAttribute</c>):
///
/// <para>
/// (b) formato canónico de <see cref="RateCounterKey"/>: solo se verifica para las claves que
/// construye <see cref="TieredRateLimitEvaluator"/> (el evaluador genérico de Fase 3+, el único
/// camino nuevo hacia adelante). Los 4 limiters pre-Fase-3 (Auth <c>LoginThrottler</c>, Connectors
/// F26 ×4, Postmaster F26, PaymentApp F26) usan sus propios formatos legacy
/// (<c>auth:failip:...</c>, <c>connectors:send:...</c>, etc.) sembrados antes de que este formato
/// canónico existiera — migrarlos ahora cambiaría claves Redis en producción (resetea contadores
/// vivos), fuera de alcance de un cierre de fitness functions. Documentado, no "arreglado" en
/// silencio.
/// </para>
///
/// <para>
/// (d) <c>AddRateLimiter</c> nativo de ASP.NET Core: el invariante §7 del plan ya admite
/// explícitamente "casos donde intencionalmente queremos gate per-instance (raro, casi ninguno)" —
/// los 6 servicios de <see cref="AllowedAddRateLimiterServices"/> son exactamente esos casos,
/// verificados uno por uno (todos son endpoints pre-auth/público/webhook sin tenant_id/user_id que
/// particionar — <c>[RateLimit]</c> ahí sería fail-open). Este test no es "cero AddRateLimiter", es
/// una allowlist congelada: cualquier <c>AddRateLimiter</c> nuevo en un servicio no listado aquí
/// rompe el build — exactamente la regresión que esta fitness function existe para atrapar.
/// Growth salió de esta lista en la auditoría independiente post-Fase-9: su único native limiter
/// ("growth-code-quote") se movió a <c>[RateLimit]</c> tiered — la premisa "JWT de servicio sin
/// user_id" que lo justificaba era falsa (el JWT M2M siempre trae TenantId).
/// </para>
/// </summary>
[Collection(RateLimitMetricsCollection.Name)]
public sealed class RateLimitFitnessFunctionsTests
{
    private static readonly HashSet<string> AllowedAddRateLimiterServices =
    [
        // Cada uno es un endpoint pre-auth/público/webhook sin tenant_id ni user_id que particionar
        // — verificado directamente en su Program.cs, no una suposición.
        "Auth", // tenant-lookup/tenant-recovery por IP (Fase A4, previo al plan de RateLimit)
        "CloudStorage", // resolución pública de ShareLink tokens — 20 req/min por IP+ruta
        "Connectors", // webhooks públicos (Fase 7) — 100 req/min por IP
        "PaymentApp", // /webhooks/* — 1000 req/min por IP (§28.4/§K.1)
        "Signature", // endpoints públicos de token — 15 req/min por IP+ruta
        "PaymentClient", // /webhooks/* y /checkout/* — 1000 req/min por IP (§28.4/§K.1)
    ];

    [Fact]
    public void No_service_uses_StringIncrementAsync_outside_RedisRateCounter()
    {
        // Patrón de llamada real (`.StringIncrementAsync(`), no una mención en doc-comment como la
        // que tiene IRateCounter.cs explicando qué NO hacer.
        var offenders = SourceFilesUnder("src")
            .Where(file => !file.EndsWith("RedisRateCounter.cs", StringComparison.Ordinal))
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"\.StringIncrementAsync\s*\("))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "IDatabase.StringIncrementAsync used outside RedisRateCounter.cs: " + string.Join(", ", offenders)
        );
    }

    [Fact]
    public void AddRateLimiter_native_only_appears_in_the_frozen_allowlist()
    {
        var addRateLimiterFiles = SourceFilesUnder("src")
            .Where(file => Path.GetFileName(file) == "Program.cs")
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"\bAddRateLimiter\s*\("))
            .ToList();

        var unexpected = addRateLimiterFiles
            .Where(file =>
                !AllowedAddRateLimiterServices.Any(svc =>
                    file.Contains(
                        $"{Path.DirectorySeparatorChar}{svc}{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal
                    )
                )
            )
            .ToList();

        Assert.True(
            unexpected.Count == 0,
            "AddRateLimiter native ASP.NET Core rate limiter found outside the frozen allowlist "
                + "(update AllowedAddRateLimiterServices if this is a deliberate new exception): "
                + string.Join(", ", unexpected)
        );
    }

    [Fact]
    public async Task TieredRateLimitEvaluator_keys_follow_the_canonical_svc_rl_policy_format()
    {
        var counter = new CapturingRateCounter();
        var evaluator = new TieredRateLimitEvaluator(
            counter,
            new FixedQuotaResolver(new EffectiveQuota(5, 60)),
            new RateLimitMetrics(),
            NullLogger<TieredRateLimitEvaluator>.Instance
        );
        var policy = new RateLimitPolicyDefinition
        {
            Name = RateLimitPolicyName.From("customer.g.create"),
            Category = RateLimitCategory.G,
            PrimaryPartition = RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
            BaseQuotaPerMinute = 5,
            WindowSeconds = 60,
            Algorithm = RateLimitAlgorithm.TokenBucket,
        };

        await evaluator.EvaluateAsync(policy, Guid.NewGuid(), Guid.NewGuid());

        var key = Assert.Single(counter.IncrementedKeys);
        Assert.Matches(@"^[a-z0-9_]+:rl:[a-z][a-z0-9_.]*:.+$", key);
    }

    private static IEnumerable<string> SourceFilesUnder(string repoRelativeDir)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        if (dir is null)
            throw new InvalidOperationException(
                "Could not locate the repo root (TaxVision.slnx) from the test output directory."
            );

        var root = Path.Combine(dir.FullName, repoRelativeDir);
        return Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            )
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            );
    }

    private sealed class FixedQuotaResolver(EffectiveQuota quota) : IRateLimitQuotaResolver
    {
        public Task<EffectiveQuota> ResolveAsync(
            RateLimitPolicyDefinition policy,
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult(quota);
    }

    private sealed class CapturingRateCounter : IRateLimitAlgorithmCounter
    {
        public List<string> IncrementedKeys { get; } = [];

        public Task<bool> EvaluateAsync(
            RateCounterKey key,
            RateLimitAlgorithm algorithm,
            int limit,
            TimeSpan window,
            CancellationToken ct = default
        )
        {
            IncrementedKeys.Add(key.Value);
            return Task.FromResult(false);
        }
    }
}
