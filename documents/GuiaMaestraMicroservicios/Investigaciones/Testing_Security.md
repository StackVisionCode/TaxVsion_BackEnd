# Testing — seguridad (autenticación, autorización, multi-tenant, superficie)

> Investigación de solo lectura, 2026-11-15. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd`. No se ejecutó ninguna prueba. Etiquetas: **[Código verificado]**, **[Doc. histórica]**, **[Recomendación]**, **[Decisión pendiente]**.

## 1. Estado actual

La seguridad se prueba en capas, igual que el modelo de 5 capas (AuthN JWT → `[AllowActorTypes]` → `[HasPermission]` → tenant boundary EF → ownership ABAC → session denylist; **[Doc. histórica]** `Implementaciones/DEVS/Guia_Creacion_Microservicio.md:596`). Hay tres familias **[Código verificado]**:

1. **Fitness de declaración** (corren en el gate): toda acción de controller declara `[AllowActorTypes]` y `[RateLimit]`/`[RateLimitExempt]`; superficie interna cerrada; IP real; ningún puerto de API publicado.
2. **Unitarias de políticas y filtros** (gate): `ActorTypeAuthorizationFilter`, `HasPermission`, `JwtTenantContextMiddleware`, denylist, módulo comercial, protección de secretos, webhooks.
3. **Aislamiento de tenant y ownership**: `TenantIsolationTests` por servicio (13), ownership por controller. **Las 13 `TenantIsolationTests` están en namespace `.Persistence.` y quedan fuera del gate** (12 con InMemory, 1 con SQL en Tasks).

## 2. Evidencia

### 2.1 Fitness de declaración **[Código verificado]**

| Prueba | Dónde | Regla |
|---|---|---|
| `<Svc>ArchitectureTests.Controller_actions_should_declare_AllowActorTypes` / `..._RateLimit_or_RateLimitExempt` | `deploy/tests/TaxVision.Notes.Tests/Architecture/NotesArchitectureTests.cs:11-76` (y "14 copias" en el monorepo) | Falla si una acción nueva queda sin actor o sin rate limit; espeja `ActorTypeAuthorizationFilter.ResolveDeclaredActorTypes` (método primero, controller como respaldo). También: controllers solo dependen de Application (123-) |
| `*ActorTypeArchitectureTests` | Auth, CloudStorage, Customer, Growth, Notification, PaymentApp, PaymentClient, Signature, Subscription, Tenant | Igual, por servicio |
| `ClientIpForwardingFitnessTests` | `BuildingBlocks.Tests/Architecture/` (5-10) | Todo `Program.cs` con `WebApplication.CreateBuilder` llama `AddTaxVisionClientIpForwarding` y `UseTaxVisionClientIp` |
| `InternalRouteConventionFitnessTests` | `BuildingBlocks.Tests/Architecture/` (7-18) | Controllers internos sin prefijo del servicio; ninguna ruta del gateway menciona `internal` |
| `DockerComposeNoPublicApiPortsFitnessTests` | `Auth.Tests/SlotGating/` (5-89) | Ninguna API publica `ports:` (excepción SFU); el gateway es el único entry point |
| `EnforcedPermissionsFitnessTests`, `ReservedPermissionsFitnessTests`, `GatedModuleFitnessTests`, `SeatSlotGatingFitnessTests` | `Auth.Tests` | Catálogo de permisos y gates |
| `ModuleGateWiringFitnessTests`, `RateLimitFitnessFunctionsTests`, `NoPlanNameLogicFitnessTests` | `BuildingBlocks.Tests` | Gate de módulo, rate limit, sin lógica por nombre de plan |
| `CatalogAdminAuthorizationFitnessTests`, `CatalogMutationAuditFitnessTests` | `Subscription.Tests` | Admin y auditoría del catálogo |

### 2.2 Unitarias de piezas compartidas **[Código verificado]**

`deploy/tests/TaxVision.BuildingBlocks.Tests/`: `ActorTypeAuthorizationFilterTests`, `HasPermissionForActorAttributeTests`, `AuthorizationMatrixTests`, `AuthorizationDenialTests`, `AuthorizationDenialObservabilityTests`, `AuthorizationMetricsTests`, `IsOwnerOrHasManageHandlerTests`, `JwtTenantContextMiddlewareTests`, `RequireRecentAuthenticationAttributeTests`, `SessionDenylist*`, `PermissionModuleMapTests`, `PermissionsSourceTests`, `ModuleGateEnforcePolicyTests`, `ModuleGateSettingsTests`, `AesGcmSecretProtectorTests`, `SecretProtectionRegistrationTests`, `JsonProtectedPayloadCodecTests`, `SensitiveNotificationContentTests`, `ClaimsPrincipalExtensionsTests`; carpetas `BotProtection/`, `Webhooks/`, `Security/`, `Session/`, `RateLimit/`.

Gateway (`deploy/tests/TaxVision.Gateway.Tests/Middleware/`): `InternalSurfaceGuardMiddlewareTests`, `MfaEnrollmentGuardMiddlewareTests`, `SeatSlotGateMiddlewareTests`, `SecurityHeadersMiddlewareTests`, `TenantHostGuardMiddlewareTests`, `TenantPropagationMiddlewareTests`, `HostTenantResolverTests`; `Pipeline/GatewayCorsPolicyTests`.

### 2.3 Por servicio **[Código verificado]**

- **Webhooks y firmas**: PaymentApp (`StripePaymentAdapterWebhookTests`, `PayPalOfflineWebhookVerifierTests`, `ProviderWebhookSecretsTests`, `HmacPurchaseIntentAmountSignerAndVerifierTests`), PaymentClient (`StripeWebhookVerificationTests`, `PayPalWebhookTests`), Connectors (`GmailPushWebhookControllerTests`, `GraphNotificationWebhookControllerTests`), Sms (`WebhooksControllerTests`), Postmaster (`Smtp2GoWebhookFeedbackTranslatorTests`), Notification (`CampaignEmailWebhookControllerTests`), Calendar (`HmacRequesterKeyHasherTests`).
- **Ownership (Capa 4b)**: `NotesControllerOwnershipTests` (`Notes.Tests/Api/NotesControllerOwnershipTests.cs:20-41`, piloto con `Authorization:ResourceOwnership:Enabled`), `ShareLinksControllerOwnershipTests` (CloudStorage), `SignatureRequestsControllerOwnershipTests`, `DraftsControllerOwnershipTests` y `A1MailboxAndDraftOwnershipTests` (Correspondence), `SeatUnitPriceOwnershipTests` (Subscription).
- **Regresiones de autorización**: `CustomerControllerAuthorizationRegressionTests`, `CustomerAvatarEndpointsAuthorizationTests`, `ServiceOnlyPolicyTests` (Customer), `SignatureAdminAuthorizationRegressionTests`, `ContentModerationAdminControllerAuthorizationTests`, `UploadSizePolicyAdminControllerAuthorizationTests` (CloudStorage), `GrowthAuthorizationTests`, `BillingAuthorizationArchitectureTests`.
- **MFA y sesiones**: `MfaEnrollmentGateMiddlewareTests`, `MfaEnrollmentSessionTests`, `TenantMfaPolicyTests`, `CustomerPortalMfaHandlerTests` (Auth y Customer), `JwtTokenGeneratorTests`, `CachedJtiDenylistTests` (Signature).
- **Formularios públicos y bots**: Tasks (`FormExternalAccessSecurityTests`, `FormTemplatesSecurityTests`, `TurnstileFormIntakeBotProtectionTests`).
- **Cifrado**: `AesGcmSensitiveDataProtectorTests` (Customer), `AesGcmRotatingSecretProtectorTests`, `EncryptedSecretProtectorTests` (Connectors), `RsaSigningKeyProviderTests` (Signature).
- **Archivos**: `FileSecurityTests`, `HttpContentClassifierTests` (CloudStorage), `SignatureImageValidatorTests`.

### 2.4 Aislamiento de tenant **[Código verificado]**

13 `TenantIsolationTests` (Auth, CloudStorage, Connectors, Correspondence, Customer, Growth, Notification, PaymentApp, PaymentClient, Postmaster, Scribe, Subscription con InMemory; Tasks con SQL Server). Patrón (`Notification.Tests/Persistence/TenantIsolationTests.cs:8-37`): `ITenantContext` falso mutable, `UseInMemoryDatabase`, escenario `Global_query_filter_isolates_tenant_A_from_tenant_B_data` sobre el `HasQueryFilter` fail-closed del DbContext. **Todas en namespace `.Persistence.` → excluidas del gate.**

### 2.5 Tokens de prueba **[Código verificado]**

`deploy/tests/TaxVision.Notes.Tests/Integration/JwtTestTokenFactory.cs:8-25`: JWT HS256 firmado con el secreto aleatorio de `NotesApiFactory` (nunca un secreto real); claims mínimos para `TryGetTenantId`/`TryGetUserId`, `[AllowActorTypes]` y `[HasPermission]`; usa `actor_type=PlatformAdmin` + rol `PlatformAdmin` para no depender de la proyección de permisos. La fábrica deja `Jwt:PublicKeyPath` vacío y pone `Jwt:Secret` (`NotesApiFactory.cs:33-42`).

## 3. Problemas

1. **El aislamiento de tenant (Capa 3a/4a) no está en el gate**: las 13 `TenantIsolationTests` viven en `.Persistence.`; 12 no necesitan infraestructura (InMemory) y podrían correr. **[Código verificado]**
2. **Tokens de prueba como PlatformAdmin** (bypass de permisos) en las T-INT: prueban el camino feliz, no la negación por permiso; la negación queda en pruebas unitarias del filtro. **[Código verificado]** **[Recomendación]**: una prueba de 403 con actor sin permiso por endpoint sensible.
3. **Sin prueba automatizada de "tenant A no ve datos de tenant B" de punta a punta por HTTP** (token de A, recurso de B → 404) en la mayoría de servicios; el filtro EF se prueba aislado. **[Código verificado]** (no se encontró patrón común) **[Decisión pendiente]**
4. Nombres en español en algunas pruebas de seguridad (`Con_el_flag_encendido_el_autor_pasa_el_chequeo_del_controller`). **[Código verificado]**
5. `TenantIsolationTests` de Notification depende de un comentario con "RBAC Fase 5" (número de fase en comentario). **[Código verificado]**

## 4. Buenas prácticas ya vigentes

- Declaración obligatoria de actor y rate limit por acción (no se puede olvidar).
- Superficie interna cerrada por comportamiento y por estructura.
- Secretos de prueba generados en memoria; ningún secreto real en las pruebas.
- Cada webhook con prueba de verificación de firma.

## 5. Recomendación

Mínimo de seguridad para un servicio nuevo:
1. `<Svc>ArchitectureTests` copiado de Notes (`[AllowActorTypes]` + `[RateLimit]` + capas de controllers).
2. `TenantIsolationTests` del DbContext (filtro global fail-closed). **[Recomendación]** ponerla fuera de `Persistence/` (por ejemplo `Infrastructure/` o `Architecture/`) si usa InMemory, para que entre al gate. **[Decisión pendiente]** confirmar con el dueño, porque rompe la costumbre actual.
3. Prueba de ownership si hay recursos con autor (patrón `NotesControllerOwnershipTests`).
4. Prueba de firma por cada webhook anónimo.
5. Endpoint M2M bajo `internal` y actor `Service`; `InternalRouteConventionFitnessTests` lo vigila.

## 6. Riesgos

- Un DbContext nuevo sin filtro global por tenant y sin `TenantIsolationTests` corriendo en el gate: fuga entre oficinas sin alarma.
- Un controller con `[AllowAnonymous]` público sin entrada en `PreAuthByIp` del gateway.
- Probar con PlatformAdmin oculta errores de permisos.

## 7. Pruebas

Ver 2.1-2.4.

## 8. Dependencias

- `BuildingBlocks.Authorization`, `BuildingBlocks.Web` (filtros, middleware), Auth (catálogo de permisos, `PermissionCatalog`), Gateway.

## 9. Material para la guía

### 9.1 Fitness de actor y rate limit

Copiar `deploy/tests/TaxVision.Notes.Tests/Architecture/NotesArchitectureTests.cs` y cambiar el ensamblado del Api. Mantener las tres pruebas: `Controller_actions_should_declare_AllowActorTypes`, `Controller_actions_should_declare_RateLimit_or_RateLimitExempt` y la de capas de controllers.

### 9.2 Aislamiento por tenant (forma)

```csharp
public sealed class TenantIsolationTests
{
    private sealed class FakeTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => _tenantId.HasValue;
        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }

    [Fact]
    public async Task Global_query_filter_isolates_tenant_A_from_tenant_B_data()
    {
        // Contexto InMemory con tenant A, sembrar A y B, afirmar que solo se ve A.
    }
}
```
Fuente: `deploy/tests/TaxVision.Notes.Tests/Persistence/NotePersistenceTests.cs:20-32`, `deploy/tests/TaxVision.Notification.Tests/Persistence/TenantIsolationTests.cs:8-37`.

### 9.3 Token de prueba

Copiar `Integration/JwtTestTokenFactory.cs` de Notes o Correspondence; firmar con el `JwtSecret` de la fábrica del servicio. Para probar 403, mintear con `actor_type` de staff sin el permiso.
