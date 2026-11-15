# Session denylist (revocación inmediata de sesiones)

> Investigación de solo lectura, 2026-11-15. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd\` salvo otra raíz indicada.
> Etiquetas: **[Código verificado]** · **[Documentación histórica]** · **[Recomendación]** · **[Decisión pendiente]**.

---

## 1. Estado actual

### 1.1 Qué problema resuelve [Código verificado]

El access token humano dura 15 min (`src/Services/Auth/Infrastructure/Security/JwtTokenGenerator.cs:24`). Revocar en la base solo corta el **próximo refresh**; sin denylist, un token ya emitido seguiría valiendo hasta su `exp`. La denylist en Redis cierra esa ventana (`src/Services/Auth/Application/Common/SessionAccessCutoff.cs:7-30`).

### 1.2 Escritura (solo Auth) [Código verificado]

- Clave Redis lógica: `auth:denylist:sid:{sessionId:N}` (GUID sin guiones), valor `true`. Con el `InstanceName = "taxvision:"` del `IDistributedCache` (`src/BuildingBlocks/BuildingBlocks.Infrastructure/Caching/CachingRegistration.cs:18`) la clave física queda `taxvision:auth:denylist:sid:{N}`.
- Escritor: `AccessTokenDenylist.DenySessionAsync(sessionId, ttl)` — `src/Services/Auth/Infrastructure/Security/AccessTokenDenylist.cs:16-21`. La misma clase implementa `ISessionDenylistReader` para Auth.
- TTL: 20 min (cubre los 15 min del token con margen) — `SessionAccessCutoff.cs:41`.
- Corte completo en tres pasos y en este orden: (1) denylist de todos los `sid` activos, (2) anuncio Pub/Sub por sesión, (3) revocación en BD — `SessionAccessCutoff.cs:44-67`. Canal: `auth:session-revoked` (`src/Services/Auth/Infrastructure/Security/RedisSessionRevocationPublisher.cs:20`); el anuncio es best-effort.
- Llamadores de `DenySessionAsync`: `SessionAccessCutoff` (baja de usuario, suspensión de tenant, corte por suscripción, restauración de admin), `Sessions/Commands/SessionCommands.cs:33,88,138` (logout, revocar otra sesión, revocar todas), `TakeoverSessionCommand.cs:77` (sesión única), `Credentials/Commands/PasswordCommands.cs:267,359`, `Users/Commands/RefreshAccessToken.cs:58,95,205` (reuso/robo de refresh), `AccountSessions/Commands/EndAccountSession.cs:44`.
- Logout propio: revoca en BD y denylista, **no publica** al socket a propósito (evita el modal "cerrada en otro dispositivo") — `SessionCommands.cs:29-33`.
- Endpoint de revocación de una sesión: `DELETE auth/sessions/{sessionId}` (`src/Services/Auth/Api/Controllers/SessionsController.cs:17,55`); todas: `DELETE auth/sessions` (`:77`).

### 1.3 Lectura en los servicios .NET [Código verificado]

- Puerto: `ISessionDenylistReader.IsSessionDeniedAsync(sessionId)` — `src/BuildingBlocks/Sessions/ISessionDenylistReader.cs:11-14`.
- Adaptador compartido: `SessionDenylistReader` lee la misma clave con `ICacheService.GetAsync<bool?>`; si Redis falla lanza `SessionDenylistUnavailableException` (no decide la política) — `src/BuildingBlocks/BuildingBlocks.Infrastructure/Sessions/SessionDenylistReader.cs:16-31`.
- `RedisCacheService.GetAsync` **propaga** el fallo de Redis (a diferencia de `GetOrCreateAsync`, que cae al origen) precisamente para la denylist — `src/BuildingBlocks/BuildingBlocks.Infrastructure/Caching/RedisCacheService.cs:54-57,72-75`. El BB-07 descartó `HybridCache` por eso (fail-open implícito y L1 sin backplane) — `RedisCacheService.cs:15-25`.
- Conexión Redis: `AbortOnConnectFail=false` + `BacklogPolicy.FailFast` (sin Redis cada comando falla al instante, no espera 5 s) — `src/BuildingBlocks/BuildingBlocks.Infrastructure/Redis/RedisConnectionRegistration.cs:27-42`.
- Middleware: `SessionDenylistMiddleware` — `src/BuildingBlocks/BuildingBlocks.Web/Session/SessionDenylistMiddleware.cs:20-78`:
  - Se salta si `SessionDenylist:Enabled=false`, si el usuario no está autenticado o si **no hay claim `sid`** (tokens M2M) (`:28-36`).
  - Denegada → **401** `{ type: "Auth.SessionRevoked" }` (`:74-77`).
  - Redis caído → según `SessionDenylist:FailureMode`: `FailOpen` (default) deja pasar; `FailClosed` → **503** `Auth.SessionDenylistUnavailable`. Ambos cuentan `authz.session_denylist_unavailable{mode}` y loguean warning (`:43-65`).
- Opciones: sección `SessionDenylist` con `Enabled` (default `true`) y `FailureMode` (default `FailOpen`) — `SessionDenylistOptions.cs:6-36`.
- Registro: `services.AddSessionDenylist(configuration)` (requiere `AddRedisCache` antes; registra `AuthorizationMetrics` con `TryAdd`) — `SessionDenylistServiceCollectionExtensions.cs:21-31`. **No usar en Auth** (Auth registra su `AccessTokenDenylist`).
- Posición en el pipeline: entre `UseAuthentication` (+ `JwtTenantContextMiddleware`) y `UseAuthorization` — `src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/Program.cs:148-154`.
- Cobertura: `AddSessionDenylist` + `UseMiddleware<SessionDenylistMiddleware>` en 25 servicios .NET además de Auth (Billing, Calendar, Campaigns, Catalog, CloudStorage, Connectors, Correspondence, Customer, Documents, FinancialInsights, Growth, Inventory, Notes, Notification, PaymentApp, PaymentClient, Postmaster, Reminder, Scribe, Signature, Sms, Subscription, Tasks, Tenant, Wallet) — búsqueda en `src/Services/*/…Api/Program.cs`.
- Configuración real: **ningún** `appsettings*.json` ni el compose define `SessionDenylist` → todos los .NET corren `Enabled=true, FailOpen` (búsqueda en `src/Services/**/appsettings*.json` y `deploy/docker/docker-compose.yml`).
- El **Gateway no consulta la denylist** (solo valida el JWT): `src/Gateway/TaxVision.Gateway/Program.cs:158-167`. Cada servicio es responsable.

### 1.4 WebSockets (Communication, Node) [Código verificado]

- Handshake: verifica JWT por JWKS, rechaza tokens con `surface` y consulta Redis con `MGET` de `taxvision:auth:denylist:jti:{jti}` y `…:sid:{sid sin guiones}` — `src/Services/Communication/src/infrastructure/jwks/jwt-verifier.ts:85-120`.
- Redis caído en el handshake: **fail-closed por defecto** (`Auth.DenylistUnavailable`), escotilla `COMMUNICATION_SESSION_DENYLIST_FAIL_CLOSED=false` — `jwt-verifier.ts:106-119`, `src/Services/Communication/src/infrastructure/config.ts:22,35,341`.
- Socket vivo: el watcher se suscribe a `auth:session-revoked`, emite `session.revoked` al room `t:{tenant}:u:{user}` y llama `sessions.revoke(message)` — `src/Services/Communication/src/infrastructure/redis/session-denylist-watcher.ts:23-82`, `src/Services/Communication/src/main.ts:513`.
- `registerSessionHandlers` (INV-09): cierra el socket exacto de la sesión revocada (o todos los del usuario si el mensaje no trae `sid`), lo saca de reuniones/llamadas, y además cierra cada socket al `exp` de su token con aviso previo `session.expiring` y renovación `auth.refresh` sin reconectar — `src/Services/Communication/src/api/socket/handlers/session-handlers.ts:25-46,48-117,129-165`.
- La clave `…:jti:` no tiene escritor en el repo (Auth solo escribe `sid`); es inocua.

---

## 2. Evidencia documental

- `documents/RBAC_Endurecimiento_Defensa_Senior.md` §FASE 6 ("fail-open si Redis se cae") y §6.7 ("`POST /auth/sessions/{sid}/revoke`") [Documentación histórica].
- Vault `C:\Users\devcacg\Desktop\Implementaciones\CommunicationCorrecciones\Investigaciones\INV-09_Sesiones_Seguridad.md`: describía que tras el handshake nada re-validaba el socket y que el watcher solo emitía; el código actual ya lo corrigió (`session-handlers.ts:25-27`) [Documentación histórica superada].
- Memoria: `project_rbac_fase6_session_denylist_completo.md`, `project_single_session_realtime_plan.md`.

---

## 3. Problemas detectados

1. **Políticas distintas ante Redis caído**: .NET `FailOpen` (implícito, sin config en ningún sitio) vs Communication `FailClosed`. No hay documento que fije la decisión de producto. **[Decisión pendiente]**.
2. **Doc §6.7 con ruta incorrecta**: no existe `POST /auth/sessions/{sid}/revoke`; es `DELETE auth/sessions/{sessionId}` (`SessionsController.cs:55`).
3. **Orden distinto en el logout**: `LogoutHandler` revoca en BD **antes** de denylistar (`SessionCommands.cs:32-33`), al revés que `SessionAccessCutoff`. Sin impacto real (no publica), pero rompe la regla documentada "primero se cierra la puerta".
4. **Tokens M2M no son revocables** (sin `sid`): un secreto M2M filtrado vale hasta 10 min por token y sin límite mientras el secreto no se rote. Ver M2M_ServiceAuth.md.
5. **Denylist sin `jti`**: solo se revoca por sesión, no por token individual (Communication busca `jti` pero nadie lo escribe).
6. **Dependencia dura de la misma instancia Redis y del mismo `InstanceName`** (`taxvision:`): un servicio con otra cadena Redis o con `InstanceName` distinto no vería las revocaciones y **no falla** (simplemente nunca encuentra la clave).

---

## 4. Buenas prácticas observadas

- La política (seguir o cortar) vive en el middleware configurable; el adaptador solo informa (H-06).
- Métrica y log con throttling cuando Redis no responde (`RedisCacheService.cs:31,136-145`).
- Orden "denylist → aviso → BD" con test (`SessionAccessCutoffTests.The_denylist_entry_exists_before_the_announcement_goes_out`).
- TTL de la entrada > vida del token (test `The_denylist_ttl_outlives_an_access_token`).
- Sockets cerrados al `exp` y expulsión inmediata ante revocación.

---

## 5. Recomendación

- Todo servicio .NET que acepte JWT humano: `AddRedisCache` + `AddSessionDenylist` + `UseMiddleware<SessionDenylistMiddleware>()` entre autenticación y autorización. Misma `ConnectionStrings:Redis` que Auth.
- Dejar el `FailureMode` **explícito** en compose aunque sea el default, para que la decisión sea visible. **[Recomendación]**
- Fijar en la guía una única decisión (fail-open .NET vs fail-closed sockets) o justificar la diferencia. **[Decisión pendiente]**
- Servicios con WebSocket propio: validar denylist en el handshake, cerrar al `exp` y suscribirse a `auth:session-revoked`.

---

## 6. Riesgos

| Riesgo | Efecto |
|---|---|
| Servicio nuevo sin el middleware | Acepta tokens revocados hasta 15 min (baja de empleado, tenant suspendido, robo de refresh) |
| Redis distinto / `InstanceName` distinto | Revocaciones invisibles, sin error |
| Redis caído con FailOpen | Ventana de 15 min sin revocación (contada en métrica) |
| Redis caído con FailClosed | 503 en todo el tráfico autenticado del servicio |
| Gateway sin denylist | Si un servicio se olvida del middleware, nada lo compensa aguas arriba |

---

## 7. Pruebas

**Existentes [Código verificado]**
- `deploy/tests/TaxVision.BuildingBlocks.Tests/Session/SessionDenylistMiddlewareTests.cs`: `Returns_401_when_the_session_is_in_the_denylist`, `Passes_through_when_the_session_is_not_in_the_denylist`, `Ignores_service_tokens_that_have_no_sid_claim`, `H06_con_FailOpen_deja_pasar_si_el_store_no_responde`, `H06_con_FailClosed_responde_503_si_el_store_no_responde`.
- `…/Session/SessionDenylistReaderTests.cs`.
- `deploy/tests/TaxVision.Auth.Tests/Application/SessionAccessCutoffTests.cs`: `Cutting_a_tenant_denylists_every_session_announces_it_and_then_revokes`, `The_denylist_entry_exists_before_the_announcement_goes_out`, `The_denylist_ttl_outlives_an_access_token`, `A_tenant_with_no_live_sessions_still_revokes_without_announcing`, `Cutting_a_user_uses_the_same_three_steps`.
- Communication: tests Vitest del verificador y de los handlers de sesión (no listados en detalle en esta investigación).

**Que debe tener un servicio nuevo [Recomendación]**
1. Fitness: el `Program.cs` registra `AddSessionDenylist` y usa `SessionDenylistMiddleware` después de `UseAuthentication` y antes de `UseAuthorization`.
2. Integración (`.Integration.`): JWT con `sid` denylistado en Redis real → 401 `Auth.SessionRevoked`.
3. Token M2M (sin `sid`) → no consulta Redis.

---

## 8. Dependencias

Redis compartido (`ConnectionStrings:Redis`, `InstanceName` `taxvision:`), Auth (escritor y publicador Pub/Sub), `BuildingBlocks.Infrastructure.Caching`, `BuildingBlocks.Web.Session`, Communication (sockets).

---

## 9. Por qué un servicio funcional puede quedar inaccesible

- `FailureMode=FailClosed` con Redis caído → 503 en todo request autenticado.
- `AddSessionDenylist` sin `AddRedisCache` previo → falla al resolver `ICacheService` (excepción en el primer request).
- Usuario cuya sesión fue denylistada por reuso de refresh (`RefreshAccessToken.cs:58,95`) → 401 en todos los servicios hasta volver a iniciar sesión (comportamiento correcto, pero suele confundirse con un bug del servicio).

---

## 10. Material para la guía (servicio de referencia: FinancialInsights)

`Program.cs` (`:51-53,148-154`):
```csharp
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddSessionDenylist(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
// ...
app.UseAuthentication();
app.UseMiddleware<BuildingBlocks.Web.Tenancy.JwtTenantContextMiddleware>();
app.UseMiddleware<SessionDenylistMiddleware>();
app.UseAuthorization();
```

Configuración (compose del servicio):
```yaml
ConnectionStrings__Redis: redis:6379
# Opcional, hoy implícito en todos los .NET:
# SessionDenylist__Enabled: "true"
# SessionDenylist__FailureMode: FailOpen
```

Respuestas que el frontend debe distinguir:
- 401 `Auth.SessionRevoked` → cerrar sesión local.
- 401 `Auth.TokenStale` → refrescar token (no es revocación; ver Auth_RBAC.md).
- 503 `Auth.SessionDenylistUnavailable` → solo si el servicio está en `FailClosed`.
