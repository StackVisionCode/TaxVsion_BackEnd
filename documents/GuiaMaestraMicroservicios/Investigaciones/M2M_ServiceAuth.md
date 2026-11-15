# M2M / ServiceAuth (client credentials entre microservicios)

> Investigación de solo lectura, 2026-11-15. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd\` salvo otra raíz indicada.
> Etiquetas: **[Código verificado]** · **[Documentación histórica]** · **[Recomendación]** · **[Decisión pendiente]**.
> Ningún valor de secreto se reproduce aquí; solo nombres de claves y variables.

---

## 1. Estado actual

### 1.1 Emisión en Auth [Código verificado]

- Endpoint: `POST auth/service-token`, `[AllowAnonymous]`, `[RateLimitExempt(...)]` — `src/Services/Auth/Api/Controllers/AuthController.cs:124-143`. Solo se pide dentro de la red (`http://auth-api:8080`): el Gateway le responde 404 (ver §1.4).
- Cuerpo: `{ clientId, clientSecret, tenantId }` → respuesta `{ accessToken, expiresInSeconds, tokenType }` — `src/Services/Auth/Application/ServiceTokens/Commands/IssueServiceToken.cs:13-15`.
- Validación (`IssueServiceTokenHandler.Handle`, `IssueServiceToken.cs:19-61`):
  - Primero el freno por dirección: si `IServiceClientThrottler.GetRetryAfterAsync(request.IpAddress)` devuelve espera → `Auth.LockedOut` (429 con `Retry-After`) sin evaluar el secreto (`:28-32`).
  - `tenantId` vacío → `Auth.InvalidClient`.
  - Busca el cliente por `ClientId` exacto en `ServiceAuth:Clients`; compara el secreto con `CryptographicOperations.FixedTimeEquals`; misma respuesta si el cliente no existe o el secreto no coincide; en ambos casos cuenta un fallo para la dirección (`RegisterFailureAsync`, `:42-48`).
  - **No restringe el tenant**: cualquier cliente registrado puede pedir un token para cualquier `tenantId`, incluido `PlatformTenant`.
- Clientes: configuración, no base de datos — `src/Services/Auth/Application/ServiceTokens/ServiceAuthOptions.cs:7-24`:
  - `ServiceAuth:Clients:N:{ClientId, Secret, Permissions:[…], Audience (default "TaxVision.Services"), Scopes:[…]}`.
  - `ServiceAuth:TokenLifetimeMinutes` (default 10).
  - Bind en `src/Services/Auth/Infrastructure/DependencyInjection.cs:53`.
- Token emitido (`GenerateScopedServiceToken`, `src/Services/Auth/Infrastructure/Security/JwtTokenGenerator.cs:119-152`): `sub` determinista por `clientId` (`:179-180`), `tenant_id` pedido, `actor_type=Service`, `client_id`, `perm` × N, `scope` × N; sin `sid` ni `perm_v`. Mismo issuer y firma que los humanos.

### 1.2 Adquisición en el servicio llamador [Código verificado]

- Contrato compartido `IServiceTokenAcquirer.GetTokenAsync(Guid tenantId)` — `src/BuildingBlocks/BuildingBlocks.Infrastructure/Security/IServiceTokenAcquirer.cs:10-13`.
- Helper HTTP común `HttpClient.RequestServiceTokenAsync(clientId, secret, tenantId)` → `ServiceTokenGrant(AccessToken, ExpiresAtUtc)`; lanza `ServiceTokenAcquisitionException` en fallo de red/HTTP/JSON y **re-lanza la cancelación del llamador** (BB-16) — `ServiceTokenHttpAcquisition.cs:22-63`, `ServiceTokenGrant.cs`.
- Caché: `ExpiringValueCache<TKey,TValue>(refreshBuffer)` (`src/BuildingBlocks/Security/ExpiringValueCache.cs:14-34`). Patrón correcto en Calendar: caché **estática** por `tenantId` con margen de 30 s (porque `AddHttpClient<>` registra el acquirer como transient) — `src/Services/Calendar/TaxVision.Calendar.Infrastructure/RateLimiting/ServiceTokenAcquirer.cs:35-72`.
- FinancialInsights: `FinancialServiceTokenAcquirer` **sin caché** (un `POST auth/service-token` por cada uso) — `src/Services/FinancialInsights/TaxVision.FinancialInsights.Infrastructure/Permissions/FinancialServiceTokenAcquirer.cs:14-30`; `FinancialPermissionsSnapshotClient` pide su propio token en cada pull — `FinancialPermissionsSnapshotClient.cs:12-29`.
- Clave de configuración del cliente (no hay convención única): 18 servicios usan `ServiceAuthClient:{ClientId,ClientSecret}` (FI: `FinancialServiceTokenAcquirer.cs:16-17`; Calendar: `ServiceAuthClientOptions.SectionName = "ServiceAuthClient"`), y 6 usan `{Servicio}:ServiceAuth:*` (Correspondence, Postmaster, Reminder, Scribe, Signature, Subscription) — conteo en `deploy/docker/docker-compose.yml`.
- URL de Auth: FI usa `Services:Auth:BaseUrl` (`DependencyInjection.cs:78,85`); Calendar `ServiceAuthClient:AuthBaseUrl` (default `http://localhost:5124`).

### 1.3 Tenant del token y PlatformTenant [Código verificado]

- Llamadas por tenant: el acquirer pide el token con el `tenantId` del caso de uso; el destino toma el tenant del token (`User.TryGetTenantId`), p. ej. `InternalCustomersController.List` — `src/Services/Customer/TaxVision.Customer.Api/Controllers/InternalCustomersController.cs:48-55`.
- Llamadas cross-tenant (reconciliaciones): token pedido para `PlatformTenant.Id` (`src/BuildingBlocks/Tenancy/PlatformTenant.cs:11`), p. ej. `FinancialTenantTimeZoneTokenProvider` (`src/Services/FinancialInsights/TaxVision.FinancialInsights.Infrastructure/Tenants/FinancialTenantTimeZoneTokenProvider.cs:8-13`) y `FinancialPlatformTokenProvider` (`Customers/CustomerDirectoryReconciliationJobs.cs:228-232`).
- Destinos que exigen PlatformTenant además de `ServiceOnly`: `internal/customers/reconciliation` y `assignments/reconciliation` (`InternalCustomersController.cs:82-83,108-109`), `internal/users/{owners,staff,portal-accounts,directory}/reconciliation` (`src/Services/Auth/Api/Controllers/InternalUserReconciliationController.cs:31,50,69,92`).
- Destinos cross-tenant **sin** comprobación de PlatformTenant: `internal/tenants/directory` (`src/Services/Tenant/TaxVision.Tenant.Api/Controllers/InternalTenantDirectoryController.cs:27-47`), `internal/tenants/{t}/users/{u}/permissions-snapshot` (`src/Services/Auth/Api/Controllers/InternalPermissionsSnapshotController.cs:18-35`), `internal/tenants/{t}/entitlements` (`src/Services/Subscription/TaxVision.Subscription.Api/Controllers/Internal/InternalTenantEntitlementsController.cs:18-33`). Estos dos últimos usan el `tenantId` de la **ruta**.

### 1.4 Autorización M2M en el destino [Código verificado]

- Policy `ServiceOnly` = `RequireClaim("actor_type","Service")`, definida localmente en cada `Program.cs` que la usa: Auth (`src/Services/Auth/Api/Program.cs:170-172`), Billing (:74), Connectors (:84), Customer (:95), Documents (:83), PaymentApp (:76), Postmaster (:100), Subscription (:116), Tenant (:111), Wallet (:60).
- Patrón de controller interno: `[Route("internal/…")]` + `[Authorize(Policy = "ServiceOnly")]` + `[AllowActorTypes(ActorType.Service)]` + `[RateLimitExempt("…")]` (ej. `InternalTenantDirectoryController.cs:27-34`).
- Permisos M2M: claim `perm` del token; `ProjectionPermissionsSource` lo lee directo para `actor_type=Service` (`src/BuildingBlocks/BuildingBlocks.Web/ActorTypeAuthorization/ProjectionPermissionsSource.cs:56-57`). Ej.: Notification lleva `scribe.render`, `cloudstorage.file.*`, `sms.send`… (compose `ServiceAuth__Clients__0__Permissions__*`).
- Scopes + audiencia propia (modelo de Growth/Billing/Documents/Payments): `[HasServiceScope]` con prefijo `service-scope:` (`src/Services/Growth/TaxVision.Growth.Api/Authorization/GrowthAuthorization.cs:17-19`), clientes con `Audience: taxvision-growth` + `Scopes` (compose `ServiceAuth__Clients__9..13`), y el destino acepta la audiencia vía `Jwt__ValidAudiences__1` (compose líneas 2300-2301, 2369-2370, 2421-2422, 2482-2483).
- Gate de módulo y rate limit no aplican al actor `Service` (`TenantModuleEntitlementsSource.cs:23`; los controllers internos son `[RateLimitExempt]`).
- Denylist no aplica (sin `sid`) — `SessionDenylistMiddleware.cs:28-32`.
- El Gateway devuelve 404 a cualquier path con segmento `internal` y al grant `/auth/service-token` (`src/Gateway/TaxVision.Gateway/Middleware/InternalSurfaceGuardMiddleware.cs:16-73`, ADR_019): ni los controllers M2M ni la emisión de tokens se alcanzan desde Internet.
- Freno de dominio del grant: `IServiceClientThrottler` → `ServiceClientThrottler` (`src/Services/Auth/Infrastructure/Security/ServiceClientThrottler.cs`): 20 fallos de credenciales por dirección en una ventana fija de 15 min (Redis, `auth:service-token-fail-ip:{ip}`). Cuenta por IP y nunca por `clientId` (contar por cliente dejaría bloquear a propósito el token de un servicio real) y, si Redis no responde, deja pasar: el grant no puede caerse con Redis.

### 1.5 Configuración y secretos [Código verificado]

- **Compose (Auth)**: `ServiceAuth__Clients__0` … `ServiceAuth__Clients__31` (`deploy/docker/docker-compose.yml:520-715`). El 31 es FinancialInsights: `ClientId: ${FINANCIAL_INSIGHTS_SERVICE_CLIENT_ID:-financial-insights-worker}`, `Secret: ${FINANCIAL_INSIGHTS_SERVICE_CLIENT_SECRET…}`, `Permissions__0: cloudstorage.file.view`, `Permissions__1: cloudstorage.file.download` (:712-716).
- **Compose (servicio)**: `ServiceAuthClient__ClientId` / `ServiceAuthClient__ClientSecret` con las **mismas** variables, `Services__Auth__BaseUrl: http://auth-api:8080/` (`:1645-1652`).
- **GitHub**: `production` está en el tope de 100 secrets; los nuevos van al environment **`production-extra`**, que solo lee el job `deploy` (`.github/workflows/deploy.yml:14-16,180,562,586-600`). FI: `FINANCIAL_INSIGHTS_SERVICE_CLIENT_ID`, `FINANCIAL_INSIGHTS_SERVICE_CLIENT_SECRET` (`:599-600`); Calendar: `CALENDAR_SERVICE_CLIENT_SECRET` (`:648`).
- **Local**: user-secrets de cada servicio. Auth: `ServiceAuth:Clients:N:ClientId|Secret|Permissions:i|Audience|Scopes:i`. Servicio: la clave exacta que bindea su clase de opciones (memoria `feedback_m2m_config_key_binding.md`).
- Clave JWT: `JWT_PRIVATE_KEY_PEM_B64` / `JWT_PUBLIC_KEY_PEM_B64` → `/run/secrets/jwt-*.pem` (`deploy.yml:529-538`).

---

## 2. Evidencia documental

- `docs/audit/09-Security-Audit.md` SEC-001: "PaymentApp confía en cualquier service actor" (ServiceOnly solo exige `actor_type=Service`) [Documentación histórica, sigue aplicando al patrón general].
- Vault `C:\Users\devcacg\Desktop\Implementaciones\CommunicationCorrecciones\Proyectos\Calendar\Evidencias\E-12_Reserva_con_la_oficina_y_secreto_M2M_2026-11-10.md`: `deploy.yml` no escribía `CALENDAR_SERVICE_CLIENT_SECRET`; Auth y Calendar quedaban con secreto vacío y todo el M2M de Calendar se apagó en silencio en producción.
- Memoria: `feedback_m2m_config_key_binding.md` (clave de user-secrets mal escrita → 401 `Auth.InvalidClient` que aflora tres capas después), `project_subscription_m2m_serviceauth_and_pullrecovery_gap.md` (sin `Subscription:ServiceAuth` el pull-recovery nunca funcionó → 403), `project_billing_m2m_platform_client_cerrado.md`.

---

## 3. Problemas detectados

1. **PlatformTenant no es una frontera entre clientes**: Auth emite token de `PlatformTenant` a cualquier cliente registrado (`IssueServiceToken.cs:26-51`). La comprobación `tenantId == PlatformTenant.Id` en los destinos solo prueba "es un cliente M2M que pidió la plataforma".
2. **Inconsistencia en endpoints cross-tenant**: Customer y Auth (reconciliaciones) exigen PlatformTenant; Tenant (`directory`), Auth (`permissions-snapshot`) y Subscription (`entitlements`) no. **[Decisión pendiente]**.
3. **`ServiceOnly` sin allowlist de cliente/scope** en la mayoría de destinos (SEC-001). Cualquier secreto M2M filtrado abre todos los `internal/*` con `ServiceOnly`.
4. **FinancialInsights no cachea tokens** (`FinancialServiceTokenAcquirer.cs:14-30`): carga extra a Auth y latencia en cada pull de permisos, lectura de directorio, entitlements y plan-rate-limits.
5. **Claves de configuración heterogéneas** (`ServiceAuthClient:*` vs `{Servicio}:ServiceAuth:*`): una errata no rompe el arranque, el acquirer devuelve `null` en silencio (`ServiceTokenAcquirer.cs:48-52` de Calendar lo loguea; FI devuelve null sin log en `FinancialServiceTokenAcquirer.cs:18-19`).
6. **Un solo acquirer para destinos con modelos distintos** (Subscription funnelea Growth-scopes y ServiceOnly por el mismo cliente; memoria `project_subscription_m2m_serviceauth_and_pullrecovery_gap.md`).
7. **Sin revocación de tokens M2M** (no hay `sid`); el corte es rotar el secreto en Auth y en el llamador a la vez.

---

## 4. Buenas prácticas observadas

- Comparación de secreto en tiempo constante y respuesta idéntica para cliente desconocido o secreto erróneo.
- Tokens cortos (10 min) y caché con margen de refresco.
- `HttpClient` tipado con `AllowAutoRedirect = false` (FI `DependencyInjection.cs:200`) y timeouts explícitos.
- Los clientes M2M de solo-lectura no lanzan: devuelven `null` y el llamador decide (fail-closed en autorización, reintento en jobs).
- Fitness de despliegue: todo `*_CLIENT_SECRET` del compose debe escribirse en `deploy.yml`; los de `production-extra` en el job `deploy`.
- Superficie `internal/*` y grant `/auth/service-token` nunca expuestos por el Gateway (`InternalSurfaceGuardMiddleware`), con pruebas; freno de dominio por dirección en el grant (`IServiceClientThrottler`).

---

## 5. Recomendación

- Cliente M2M propio por servicio (`{servicio}-worker`), registrado en Auth con los **mínimos** `Permissions`/`Scopes` y la audiencia por defecto salvo que el destino use scopes.
- Acquirer con `ExpiringValueCache` estática por `tenantId` (patrón Calendar), clave de config `ServiceAuthClient:*` y log de advertencia cuando falta.
- Para endpoints cross-tenant: exigir PlatformTenant **y** documentar que eso no distingue clientes. **[Decisión pendiente]**: allowlist por `client_id` o scope en destinos sensibles (SEC-001).
- Secretos nuevos: environment `production-extra`, línea en el job `deploy` de `deploy.yml`, y caso en `ProductionExtraSecretsDeployTests`.

---

## 6. Riesgos

| Riesgo | Efecto |
|---|---|
| Secreto ausente en `deploy.yml` (caso Calendar E-12) | Todo el M2M del servicio se apaga en silencio en producción |
| Clave de config mal escrita | `Auth.InvalidClient` → pull-recovery null → 403 a humanos; reconciliaciones abortan |
| Cliente no registrado en Auth (`ServiceAuth:Clients:N`) | Igual que arriba |
| Secreto M2M filtrado | Acceso a todos los `internal/*` `ServiceOnly` de todos los tenants (incl. PlatformTenant) |
| Índice `N` repetido o saltado en `ServiceAuth:Clients:N` | Un cliente pisa a otro en el bind (riesgo de merge, ver memoria `feedback_merge_companero_permisos_y_migraciones.md`) |

---

## 7. Pruebas

**Existentes [Código verificado]**
- `deploy/tests/TaxVision.Auth.Tests/Application/IssueServiceTokenHandlerTests.cs`: `Configured_audience_and_scopes_are_forwarded_to_the_token_generator`.
- `deploy/tests/TaxVision.Auth.Tests/Infrastructure/JwtTokenGeneratorTests.cs`.
- `deploy/tests/TaxVision.BuildingBlocks.Tests/Deploy/ServiceClientSecretsDeployWiringTests.cs`: `Every_client_secret_the_compose_reads_is_written_by_the_deploy_workflow`.
- `…/Deploy/ProductionExtraSecretsDeployTests.cs`: `Secret_kept_in_production_extra_is_written_by_the_deploy_job` (incluye `FINANCIAL_INSIGHTS_SERVICE_CLIENT_ID/SECRET`, `CALENDAR_SERVICE_CLIENT_SECRET`).
- `…/Security/ExpiringValueCacheTests.cs`.
- `…/Architecture/InternalRouteConventionFitnessTests.cs`: `Todo_Route_con_segmento_internal_empieza_por_internal`, `Ninguna_ruta_del_Gateway_menciona_internal`.
- FI: `deploy/tests/TaxVision.FinancialInsights.Tests/Infrastructure/PermissionsSnapshotClientTests.cs` (`Pulls_existing_users_effective_codes_from_auth_with_service_token`), `TenantDirectoryReconciliationTests.Client_reads_status_and_version_with_a_platform_token`, `Client_returns_null_when_tenant_fails_or_there_is_no_token`, `UpstreamClientTests.cs`.

**Que debe tener un servicio nuevo [Recomendación]**
1. Acquirer: sin config → null + warning; con config → un solo POST para N llamadas dentro de la vida del token; cancelación del llamador se propaga.
2. Cada cliente HTTP M2M: token ausente → null sin llamar al destino; 401/403/5xx → null.
3. Endpoints `internal/*` propios: token humano → 403; token Service de otro tenant → 403 si es cross-tenant y exige PlatformTenant.
4. Fitness de despliegue (ya existen; el servicio debe aparecer en ellos).

---

## 8. Dependencias

Auth (`ServiceAuth:Clients`, `POST auth/service-token`, firma JWT), destinos `internal/*` (Auth, Tenant, Customer, Subscription, CloudStorage…), `BuildingBlocks.Infrastructure.Security`, `BuildingBlocks.Security.ExpiringValueCache`, compose, `deploy.yml`, GitHub environments `production` / `production-extra`, user-secrets locales.

---

## 9. Por qué un servicio funcional puede quedar inaccesible

- Sin cliente M2M válido: el pull-recovery de permisos devuelve `null` → los usuarios que el servicio no vio por eventos reciben 403 (`ProjectionPermissionsSource.cs:79-111`).
- Sin token de PlatformTenant: las reconciliaciones (zona horaria, asignaciones, directorios) abortan → filtros de visibilidad vacíos (empleados sin clientes visibles), nombres faltantes, zona UTC por defecto.
- Sin token para `internal/plan-rate-limits`: el rate limit cae a la cuota base sin escalar (no bloquea, pero un tenant grande puede recibir 429 antes de lo que su plan permite).
- Permiso M2M que falta en `ServiceAuth:Clients:N:Permissions` → 403 en el destino que exige `[HasPermission]` (caso Notification → `scribe.render`).

---

## 10. Material para la guía (servicio de referencia: FinancialInsights + caché de Calendar)

### 10.1 Registrar el cliente en Auth (compose de `auth-api`)
```yaml
# deploy/docker/docker-compose.yml:712-716 (secreto omitido)
ServiceAuth__Clients__31__ClientId: ${FINANCIAL_INSIGHTS_SERVICE_CLIENT_ID:-financial-insights-worker}
ServiceAuth__Clients__31__Secret: ${FINANCIAL_INSIGHTS_SERVICE_CLIENT_SECRET:-}
ServiceAuth__Clients__31__Permissions__0: cloudstorage.file.view
ServiceAuth__Clients__31__Permissions__1: cloudstorage.file.download
```
Local (user-secrets de Auth): `ServiceAuth:Clients:31:ClientId`, `ServiceAuth:Clients:31:Secret`, `ServiceAuth:Clients:31:Permissions:0`…

### 10.2 Configurar el llamador (compose del servicio)
```yaml
Services__Auth__BaseUrl: http://auth-api:8080/
ServiceAuthClient__ClientId: ${FINANCIAL_INSIGHTS_SERVICE_CLIENT_ID:-financial-insights-worker}
ServiceAuthClient__ClientSecret: ${FINANCIAL_INSIGHTS_SERVICE_CLIENT_SECRET:-}
```
Local (user-secrets del servicio): `ServiceAuthClient:ClientId`, `ServiceAuthClient:ClientSecret`, `Services:Auth:BaseUrl`.

### 10.3 GitHub
Secrets nuevos en el environment `production-extra` y la línea `VAR=${{ secrets.VAR }}` en el paso "Append production-extra secrets to .env" del job `deploy` (`.github/workflows/deploy.yml:591-600`). Añadir el caso a `ProductionExtraSecretsDeployTests`.

### 10.4 Acquirer con caché (patrón Calendar)
`src/Services/Calendar/TaxVision.Calendar.Infrastructure/RateLimiting/ServiceTokenAcquirer.cs:35-72`:
```csharp
internal sealed class ServiceTokenAcquirer(HttpClient http, IOptions<ServiceAuthClientOptions> options,
    ILogger<ServiceTokenAcquirer> logger) : IServiceTokenAcquirer
{
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromSeconds(30);
    private static readonly ExpiringValueCache<Guid, string> _cache = new(RefreshBuffer);

    public async Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default)
    {
        var opt = options.Value;
        if (string.IsNullOrWhiteSpace(opt.ClientId) || string.IsNullOrWhiteSpace(opt.ClientSecret))
        {
            logger.LogWarning("ServiceAuthClient is not configured; cannot acquire a service token.");
            return null;
        }
        try
        {
            return await _cache.GetOrCreateAsync(tenantId, async innerCt =>
            {
                var grant = await http.RequestServiceTokenAsync(opt.ClientId, opt.ClientSecret, tenantId, innerCt);
                return (grant.AccessToken, grant.ExpiresAtUtc);
            }, ct);
        }
        catch (ServiceTokenAcquisitionException ex)
        {
            logger.LogWarning(ex, "Could not acquire a service token for tenant {TenantId}.", tenantId);
            return null;
        }
    }
}
```

### 10.5 Registro del `HttpClient` (FI `DependencyInjection.cs:82-88,200`)
```csharp
services.AddHttpClient<IServiceTokenAcquirer, FinancialServiceTokenAcquirer>(http =>
{
    http.BaseAddress = BaseUri(configuration, "Services:Auth:BaseUrl");
    http.Timeout = TimeSpan.FromSeconds(15);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
```

### 10.6 Endpoint interno propio (patrón)
```csharp
[ApiController]
[Route("internal/tenants")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalTenantDirectoryController(IMessageBus bus) : ControllerBase
{
    [HttpGet("directory")]
    [RateLimitExempt("M2M interno (actor_type=Service), nunca expuesto en el Gateway público.")]
    public async Task<ActionResult<IReadOnlyList<TenantResponse>>> GetDirectory(
        [FromQuery] int page = 1, [FromQuery] int size = 100, CancellationToken ct = default) { … }
}
```
Y en `Program.cs`:
```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ServiceOnly", policy => policy.RequireClaim("actor_type", "Service"));
```
Si el endpoint es cross-tenant, añadir `if (!User.TryGetTenantId(out var t) || t != PlatformTenant.Id) return Forbid();` (patrón `InternalCustomersController.cs:82-83`).
