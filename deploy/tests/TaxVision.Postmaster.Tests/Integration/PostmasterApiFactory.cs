using System.Security.Cryptography;
using JasperFx;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Domain.Sending;
using Wolverine;

namespace TaxVision.Postmaster.Tests.Integration;

/// <summary>
/// Host real de Postmaster.Api (SQL Server + Redis locales) para los tests de integración de Fase 4.4
/// del plan de rate limiting — mismo patrón que <c>TaxVision.Tenant.Tests.Integration.TenantApiFactory</c>
/// (Fase 4.2) y <c>TaxVision.Notification.Tests.Integration.NotificationApiFactory</c> (Fase 4.3). La
/// clave de firma JWT se sobreescribe con un secreto HS256 generado en memoria SOLO para este proceso de
/// test, nunca leído de ningún user-secret real ni escrito a disco.
/// </summary>
public sealed class PostmasterApiFactory : WebApplicationFactory<Program>
{
    public string JwtSecret { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    public string JwtIssuer => "TaxVision.Auth";
    public string JwtAudience => "TaxVision.Services";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration(
            (_, config) =>
                config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Jwt:PublicKeyPath"] = string.Empty,
                        ["Jwt:PublicKeyPem"] = string.Empty,
                        ["Jwt:Secret"] = JwtSecret,
                        // Aunque el dev tenga la key en user-secrets, los tests nunca consultan SMTP2GO real.
                        ["DeliveryFeedback:Smtp2Go:ApiKey"] = string.Empty,
                    }
                )
        );

        // Sin transportes externos, el host de prueba no le roba correos a la cola postmaster-events. La persistencia
        // queda: sin ella, todo handler con transacción EF lanza y los reintentos cuelgan la prueba. Los jobs consultan
        // SMTP2GO, siembran proveedores y limpian la base: fuera. Y nunca sale un correo real: los dos
        // remitentes se reemplazan por uno que se niega.
        builder.ConfigureTestServices(services =>
        {
            services.DisableAllExternalWolverineTransports();
            // Sin los sobres de la flota: MediatorOnly conserva el almacén sin migrarlo ni leerlo (BB-10).
            services.ConfigureWolverine(options =>
            {
                options.Durability.Mode = DurabilityMode.MediatorOnly;
                options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
            });
            foreach (
                var job in services
                    .Where(descriptor =>
                        descriptor.ServiceType == typeof(IHostedService)
                        && descriptor.ImplementationType?.Namespace?.StartsWith("TaxVision.", StringComparison.Ordinal)
                            == true
                    )
                    .ToList()
            )
                services.Remove(job);

            services.RemoveAll<IEmailSender>();
            services.RemoveAll<IConnectedMailboxSender>();
            services.AddSingleton<IEmailSender, RefusingSender>();
            services.AddSingleton<IConnectedMailboxSender, RefusingSender>();
        });
    }

    private sealed class RefusingSender : IEmailSender, IConnectedMailboxSender
    {
        public Task<SendResult> SendAsync(
            SentMessage message,
            RenderedContent content,
            ResolvedEmailProvider provider,
            IReadOnlyList<InlineAssetBytes> inlineAssets,
            IReadOnlyList<OutboundAttachmentBytes> attachments,
            CancellationToken ct
        ) => throw new InvalidOperationException("Integration tests never send real email.");

        public Task<SendResult> SendAsync(
            SentMessage message,
            RenderedContent content,
            ResolvedMailbox provider,
            string? inReplyToInternetMessageId,
            IReadOnlyList<string>? references,
            string? replyToProviderMessageId,
            IReadOnlyList<OutboundAttachmentBytes> attachments,
            IReadOnlyList<InlineAssetBytes> inlineAssets,
            CancellationToken ct
        ) => throw new InvalidOperationException("Integration tests never send real email.");
    }
}
