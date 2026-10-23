using BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.Infrastructure.ClientForms.Attachments;
using TaxVision.Tasks.Infrastructure.ClientForms.Audit;
using TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories;
using TaxVision.Tasks.Infrastructure.ClientForms.Security;
using TaxVision.Tasks.Infrastructure.CloudStorage;
using TaxVision.Tasks.Infrastructure.RateLimiting;

namespace TaxVision.Tasks.Infrastructure.ClientForms;

public static class ClientFormsRegistration
{
    public static IServiceCollection AddClientForms(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<ClientFormsOptions>()
            .Bind(configuration.GetSection(ClientFormsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IFormEngine>(provider => new FormEngine(
            provider.GetRequiredService<IOptions<ClientFormsOptions>>().Value.Limits
        ));
        services.AddScoped<IFormTemplateRepository, FormTemplateRepository>();
        services.AddScoped<IFormTemplateVersionRepository, FormTemplateVersionRepository>();
        services.AddScoped<IFormModuleSettingsRepository, FormModuleSettingsRepository>();
        services.AddScoped<IClientFormsAvailabilityEvaluator, ClientFormsAvailabilityEvaluator>();
        services.AddScoped<IClientFormsPublicCapabilityEvaluator, ClientFormsPublicCapabilityEvaluator>();
        services.AddHttpClient<IFormIntakeBotProtection, TurnstileFormIntakeBotProtection>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddScoped<IFormAuditLog, FormAuditLog>();
        services.AddScoped<IFormRequestRepository, FormRequestRepository>();
        services.AddScoped<IFormRequestStaffReadRepository, FormRequestStaffReadRepository>();
        services.AddScoped<IFormFileScanVerdictRepository, FormFileScanVerdictRepository>();
        services.AddScoped<IFormParticipantRepository, FormParticipantRepository>();
        services.AddScoped<IFormIntakeLinkRepository, FormIntakeLinkRepository>();
        services.AddScoped<IFormResponsibleUserValidator, FormResponsibleUserValidator>();
        services.AddScoped<IFormAccessGrantRepository, FormAccessGrantRepository>();
        services.AddScoped<IFormAccessChallengeRepository, FormAccessChallengeRepository>();
        services.AddScoped<IFormAccessSessionRepository, FormAccessSessionRepository>();
        services.AddScoped<IConsentNoticeRepository, ConsentNoticeRepository>();
        services.AddScoped<IFormConsentRecordRepository, FormConsentRecordRepository>();
        services.AddScoped<IFormConsentGate, FormConsentGate>();
        services.AddSingleton<IFormExternalAccessSecurity, FormExternalAccessSecurity>();
        services.AddMemoryCache();
        services.AddHttpClient<IFormPublicTenantHostValidator, FormPublicTenantHostValidator>(
            (provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
                client.BaseAddress = new Uri(
                    options.AuthBaseUrl.EndsWith('/') ? options.AuthBaseUrl : options.AuthBaseUrl + "/"
                );
                client.Timeout = TimeSpan.FromSeconds(10);
            }
        );
        services.AddHttpClient<IFormExternalUploadClient, FormExternalUploadClient>(
            (provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<CloudStorageClientOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl.EndsWith('/') ? options.BaseUrl : options.BaseUrl + "/");
                client.Timeout = TimeSpan.FromSeconds(30);
            }
        );
        services.AddScoped<IFormRequestVisibilityFilter, FormRequestVisibilityFilter>();
        services.AddScoped<IClientFormsCustomerReader, ClientFormsCustomerReader>();
        services.AddScoped<IClientFormsTaskReader, ClientFormsTaskReader>();
        services.AddScoped<IAnswersProtector, AnswersProtector>();
        services.AddScoped<ISensitiveValueProtector, SensitiveValueProtector>();
        services.AddScoped<FormAttachmentReconciler>();
        services.AddHostedService<FormAttachmentReconciliationJob>();
        services.AddHttpContextAccessor();
        services.AddProtectedPayloadProtection(configuration);
        return services;
    }
}
