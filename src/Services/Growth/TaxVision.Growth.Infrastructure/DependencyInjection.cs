using System.Text;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Growth.Infrastructure.Idempotency;
using TaxVision.Growth.Infrastructure.Observability;
using TaxVision.Growth.Infrastructure.Payments;
using TaxVision.Growth.Infrastructure.Persistence;
using TaxVision.Growth.Infrastructure.Persistence.Repositories.Codes;
using TaxVision.Growth.Infrastructure.Persistence.Repositories.Referrals;
using TaxVision.Growth.Infrastructure.Security;
using TaxVision.Referrals.Application.Abstractions;

namespace TaxVision.Growth.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddGrowthInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<GrowthDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<GrowthDbContext>());
        services.AddScoped<ICodeDefinitionRepository, CodeDefinitionRepository>();
        services.AddScoped<ICodeQuoteRepository, CodeQuoteRepository>();
        services.AddScoped<ICodeReservationRepository, CodeReservationRepository>();
        services.AddScoped<ICodeRedemptionRepository, CodeRedemptionRepository>();
        services.AddScoped<ICodeCompensationRepository, CodeCompensationRepository>();
        services.AddScoped<ICodeUsageCounterRepository, CodeUsageCounterRepository>();
        services.AddScoped<SqlBusinessIdempotencyExecutor>();
        services.AddScoped<IBusinessIdempotencyExecutor>(provider =>
            provider.GetRequiredService<SqlBusinessIdempotencyExecutor>()
        );
        services.AddScoped<IReferralIdempotencyExecutor, SqlReferralIdempotencyExecutor>();
        services.AddSingleton<ICodeTokenHasher, HmacSha256CodeTokenHasher>();
        services.AddSingleton<IReferralCodeTokenHasher, HmacSha256ReferralCodeTokenHasher>();
        services.AddSingleton<IReferralCodeTokenGenerator, HmacSha256ReferralCodeTokenGenerator>();
        services.AddSingleton<IPaymentOutcomeVerifier, FailClosedPaymentOutcomeVerifier>();
        services.AddScoped<IReferralProgramRepository, ReferralProgramRepository>();
        services.AddScoped<IReferralCodeRepository, ReferralCodeRepository>();
        services.AddScoped<IReferralAttributionRepository, ReferralAttributionRepository>();
        services.AddScoped<IReferralQualificationRepository, ReferralQualificationRepository>();
        services.AddScoped<IReferralRewardCaseRepository, ReferralRewardCaseRepository>();
        services.AddScoped<IReferralRewardAttemptRepository, ReferralRewardAttemptRepository>();
        services.AddScoped<IReferralRewardQuota, SqlReferralRewardQuota>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<GrowthMetrics>();

        services
            .AddOptions<CodeTokenHashingOptions>()
            .Bind(configuration.GetSection(CodeTokenHashingOptions.SectionName))
            .Validate(
                value => !string.IsNullOrWhiteSpace(value.Pepper) && Encoding.UTF8.GetByteCount(value.Pepper) >= 32,
                "Growth:Codes:TokenHashing:Pepper must contain at least 32 UTF-8 bytes."
            )
            .ValidateOnStart();
        services
            .AddOptions<ReferralCodeTokenHashingOptions>()
            .Bind(configuration.GetSection(ReferralCodeTokenHashingOptions.SectionName))
            .Validate(
                value => !string.IsNullOrWhiteSpace(value.Pepper) && Encoding.UTF8.GetByteCount(value.Pepper) >= 32,
                "Growth:Referrals:TokenHashing:Pepper must contain at least 32 UTF-8 bytes."
            )
            .ValidateOnStart();
        services
            .AddOptions<BusinessIdempotencyOptions>()
            .Bind(configuration.GetSection(BusinessIdempotencyOptions.SectionName))
            .Validate(
                value => value.RetentionDays is >= 1 and <= 36_500,
                "Growth:BusinessIdempotency:RetentionDays must be between 1 and 36500."
            )
            .ValidateOnStart();
        services
            .AddOptions<PaymentOutcomeVerifierOptions>()
            .Bind(configuration.GetSection(PaymentOutcomeVerifierOptions.SectionName))
            .ValidateOnStart();

        return services;
    }
}
