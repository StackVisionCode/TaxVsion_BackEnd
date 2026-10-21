using BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.Infrastructure.ClientForms.Audit;
using TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories;
using TaxVision.Tasks.Infrastructure.ClientForms.Security;

namespace TaxVision.Tasks.Infrastructure.ClientForms;

public static class ClientFormsRegistration
{
    public static IServiceCollection AddClientForms(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ClientFormsOptions>()
            .Bind(configuration.GetSection(ClientFormsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IFormEngine>(provider =>
            new FormEngine(provider.GetRequiredService<IOptions<ClientFormsOptions>>().Value.Limits)
        );
        services.AddScoped<IFormTemplateRepository, FormTemplateRepository>();
        services.AddScoped<IFormTemplateVersionRepository, FormTemplateVersionRepository>();
        services.AddScoped<IFormModuleSettingsRepository, FormModuleSettingsRepository>();
        services.AddScoped<IClientFormsAvailabilityEvaluator, ClientFormsAvailabilityEvaluator>();
        services.AddScoped<IFormAuditLog, FormAuditLog>();
        services.AddScoped<IFormRequestRepository, FormRequestRepository>();
        services.AddScoped<IFormFileScanVerdictRepository, FormFileScanVerdictRepository>();
        services.AddScoped<IFormRequestVisibilityFilter, FormRequestVisibilityFilter>();
        services.AddScoped<IClientFormsCustomerReader, ClientFormsCustomerReader>();
        services.AddScoped<IClientFormsTaskReader, ClientFormsTaskReader>();
        services.AddScoped<IAnswersProtector, AnswersProtector>();
        services.AddScoped<ISensitiveValueProtector, SensitiveValueProtector>();
        services.AddHttpContextAccessor();
        services.AddRotatingSecretProtection(configuration);
        return services;
    }
}
