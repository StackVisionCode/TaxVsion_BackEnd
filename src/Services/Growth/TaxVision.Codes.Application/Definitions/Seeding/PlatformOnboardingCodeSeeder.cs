using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Definitions.Seeding;

/// <summary>
/// Siembra los CÓDIGOS DE PLATAFORMA usables en el onboarding pago-primero (pre-tenant). Se crean bajo
/// <see cref="PlatformTenant"/> con <see cref="CodeOwnerScope.Platform"/> y SIN scope de oferta, para que
/// el quote-by-hash del onboarding (subject Anonymous(OnboardingId), oferta = plan) los resuelva. Se
/// ejecuta al arranque de Growth, idempotente por hash del token. Tipos MVP: Percentage y FixedAmount
/// (cubren descuento parcial, 100% y giftcard). Referido/promo/gift se distinguen en Auth por el campo de
/// entrada, no por el código en sí.
/// </summary>
public static class PlatformOnboardingCodeSeeder
{
    private sealed record Seed(string Token, string Name, Func<CodeBenefit> Benefit);

    public static async Task SeedAsync(
        ICodeDefinitionRepository definitions,
        ICodeTokenHasher hasher,
        IUnitOfWork unitOfWork,
        ILogger logger,
        CancellationToken ct = default
    )
    {
        var seeds = new[]
        {
            // 100% — para probar el carril $0 (cubierto por código, sin pago).
            new Seed("WELCOME100", "Onboarding — bienvenida 100%", () => Percentage(10_000)),
            // 20% — descuento parcial (carril con cobro del neto).
            new Seed("WELCOME20", "Onboarding — bienvenida 20%", () => Percentage(2_000)),
            // Gift fijo de US$25 (se clampa al bruto residual si es menor).
            new Seed("GIFT25", "Onboarding — gift US$25", () => Fixed(2_500, "USD")),
            // Gift del 5% para probar el carril con cobro (net>0) directo, sin referido. El token DEBE
            // tener 8+ caracteres (CodeDisplay.FromToken), por eso GIFTCARD5 y no SAVE5.
            new Seed("GIFTCARD5", "Onboarding — gift 5%", () => Percentage(500)),
        };

        var actor = PlatformTenant.Id; // actor de siembra (no vacío)
        var nowUtc = DateTime.UtcNow;
        var created = 0;

        foreach (var seed in seeds)
        {
            var hashResult = hasher.Hash(seed.Token);
            if (hashResult.IsFailure)
            {
                logger.LogWarning(
                    "Seed code '{Name}' skipped: invalid token hash ({Code}).",
                    seed.Name,
                    hashResult.Error.Code
                );
                continue;
            }

            var existing = await definitions.GetApplicableByHashAsync(PlatformTenant.Id, hashResult.Value, ct);
            if (existing is not null)
                continue; // Idempotente: ya sembrado.

            var displayResult = CodeDisplay.FromToken(seed.Token);
            if (displayResult.IsFailure)
            {
                logger.LogWarning(
                    "Seed code '{Name}' skipped: invalid display ({Code}).",
                    seed.Name,
                    displayResult.Error.Code
                );
                continue;
            }

            var definitionResult = CodeDefinition.Create(
                PlatformTenant.Id,
                CodeOwnerScope.Platform,
                tenantScopeId: null, // usable por cualquier consumidor (incl. el onboarding pre-tenant)
                seed.Name,
                CodeKind.Promotional,
                hashResult.Value,
                displayResult.Value,
                startsAtUtc: nowUtc,
                expiresAtUtc: null,
                maxRedemptions: null,
                maxRedemptionsPerTenant: null,
                maxRedemptionsPerSubject: null,
                actorUserId: actor,
                nowUtc
            );
            if (definitionResult.IsFailure)
            {
                logger.LogWarning(
                    "Seed code '{Name}' skipped: {Code} - {Message}",
                    seed.Name,
                    definitionResult.Error.Code,
                    definitionResult.Error.Message
                );
                continue;
            }

            var definition = definitionResult.Value;
            var ruleResult = definition.PublishRuleVersion(
                seed.Benefit(),
                minimumPurchase: null,
                allowStacking: true,
                actor,
                nowUtc
            );
            if (ruleResult.IsFailure)
            {
                logger.LogWarning("Seed code '{Name}' skipped: rule {Code}", seed.Name, ruleResult.Error.Code);
                continue;
            }

            var activateResult = definition.Activate(actor, nowUtc);
            if (activateResult.IsFailure)
            {
                logger.LogWarning("Seed code '{Name}' skipped: activate {Code}", seed.Name, activateResult.Error.Code);
                continue;
            }

            await definitions.AddAsync(definition, ct);
            created++;
        }

        if (created > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("Seeded {Count} platform onboarding code(s).", created);
        }
    }

    private static CodeBenefit Percentage(int basisPoints) =>
        CodeBenefit.CreatePercentage(PercentageBasisPoints.Create(basisPoints).Value).Value;

    private static CodeBenefit Fixed(long cents, string currency) =>
        CodeBenefit.CreateFixedAmount(Money.Create(cents, currency).Value).Value;
}
