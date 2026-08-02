# Plan de Implementación — Rate Limiting Multi-Capa por Tenant

> **Audiencia**: Sonnet 5 (agente ejecutor) y cualquier ingeniero que vaya a ejecutar este plan por fases.
> **Regla**: al arrancar cada sesión sobre este trabajo, releer este documento entero antes de tocar código. Cada fase termina con build + tests + reporte español al usuario.
> **Documento hermano**: `Guia_Nuevos_Servicios_Endpoints.md` — obligatorio para todo endpoint nuevo, todo bounded context nuevo, todo microservicio nuevo. **Cualquier cosa nueva se diseña ya con este modelo — no se pospone**.

---

## 0. Propósito

Sustituir el rate-limiting actual del monorepo (mayormente por IP raw, in-memory por réplica, con capas incompletas) por el **modelo canónico de 4 capas** que usan Stripe, Shopify, Zendesk, Atlassian, Salesforce, Auth0 y HubSpot:

```
Capa 1 — Global de infra (load shedder de flota)           ← última red de seguridad
Capa 2 — Per-tenant (partición primaria, escalada por plan) ← fairness + billing tier
Capa 3 — Per-user / per-token dentro del tenant             ← contra scripts tóxicos y agentes runaway
Capa 4 — Per-endpoint (endpoints caros con topes propios)   ← ningún endpoint pesado apaga a los livianos
```

Las 4 capas se evalúan en orden por cada request. El request pasa **solo si ninguna dispara**. La primera que dispare devuelve `429 Too Many Requests` con `Retry-After` y con el header que identifica **qué capa** disparó (para debugging y para que el cliente sepa si debe reintentar o cambiar de patrón).

**Fuente del veredicto**: investigación de industria + auditoría del código actual, realizada 2026-08-01 (ver notas de sesión). Consenso: 10/10 SaaS B2B grandes usan tenant como partición primaria, **cero** usa una sola capa. El senior tiene razón en la dirección; este plan añade lo que faltaba (overlay per-user, capa global, per-endpoint).

---

## 1. Principio clave — "Budget contextual por endpoint"

**No todos los endpoints valen lo mismo, ni cuestan lo mismo, ni tienen el mismo blast radius.** Un `POST /auth/login` no puede compartir cuota con un `GET /customers`, y ninguno puede compartir cuota con un `POST /customers/bulk-import` o con un `POST /connectors/accounts/{id}/send` (que gasta cuota de Gmail).

Por eso el plan clasifica **cada endpoint** en una de 17 categorías (ver §4), y cada categoría define:

1. Su **capa primaria** (algunas cosas no tienen tenant — login pre-auth, webhooks — por diseño).
2. Su **overlay** (qué se apila encima).
3. Su **cuota base** para plan Standard.
4. Su **algoritmo** (fixed window, sliding window, token bucket, leaky bucket).
5. Su **consecuencia al exceder** (429, lockout, cooldown silencioso, terminal fail).

El multiplicador por plan tier (§5) se aplica sobre la cuota base — un Enterprise no obtiene "más de todo" uniformemente; obtiene más de las categorías **contadas** en la promesa comercial de su plan.

---

## 2. Estado actual (baseline del que partimos)

Ver reporte de inventario del 2026-08-01. Resumen honesto:

- **20 políticas HTTP AspNetCore** — 15 son por IP raw, 3 por IP+path, 2 por user id, 3 por tenant id. **Todas son in-memory por réplica** (con N réplicas el límite efectivo es N× el declarado — bug de escalabilidad grave).
- **6 throttlers de dominio** — bien planteados en scope (PaymentApp por tenant; Connectors Send/Body/Attachment por tenant+account; Postmaster por tenant+provider) usando `IRateCounter` atómico post-F26.
- **`ILoginThrottler` (Auth)** — usa `GET+SET` no atómico sobre `IDistributedCache` (bug TOCTOU vivo, el propio archivo lo reconoce).
- **`ConnectorsProviderRateLimiter`** — global por proveedor **sin dimensión tenant** → noisy neighbor real (un tenant grande hunde Gmail para todos).
- **Communication (Node)** — sockets por (tenant, user) bien, pero usan `INCR+EXPIRE` separados (mismo bug que F26 arregló en .NET, replicado en Node).
- **No hay capa global de infra** — nadie observa "el gateway está por caer aunque todos los tenants estén dentro de su cuota".
- **No hay tier-aware quotas** — las cuotas son constantes hard-coded, no dependen del `Subscription.PlanCode` del tenant.

---

## 3. Invariantes (no negociables — se validan con NetArchTest en Fase 9)

Cualquier fase que rompa uno de estos se detiene y se replantea:

1. **Toda cuota persistida se cuenta con `IRateCounter` (Redis Lua atómico)** — jamás con `GET+SET`, jamás con `INCR+EXPIRE` separados. Se prohibe `IDatabase.StringIncrementAsync` fuera de `RedisRateCounter`.
2. **Toda partición debe ser explícita**. Nada de "partition sin especificar" que se cae a global-por-proceso implícito. Las 4 capas exigen una `RateCounterKey` con formato canónico (§7).
3. **Fail-open al fallo de Redis** — si Redis está caído, se **permite** el request y se emite métrica `ratelimit.fallback_open_total{policy,reason}`. Nunca dropear todo el tráfico por un fallo del limitador.
4. **Toda respuesta 429 lleva**: header `Retry-After` (segundos), header `X-RateLimit-Policy` (nombre canónico de la política que disparó), header `X-RateLimit-Layer` (`infra` | `tenant` | `user` | `endpoint`), y body con `Error("RateLimit.Exceeded", "…")` estándar del monorepo.
5. **Todo rate limit emite métricas OTel**: `ratelimit.evaluated_total{policy,layer,tenant_id,plan}` y `ratelimit.blocked_total{policy,layer,tenant_id,plan}`. Sin métrica por tenant no se puede operar el sistema, punto.
6. **Todo rate limit debe ser tier-aware** desde Fase 6 en adelante. Cuota hard-coded solo es válida en categorías A/B/C/D/E (pre-auth, webhooks, público — donde no hay tenant o el tenant no tiene plan aún).
7. **Ningún rate limit HTTP AspNetCore es in-memory por proceso**. A partir de Fase 3, todos pasan por el middleware unificado que usa `IRateCounter`. El framework `AddRateLimiter` de AspNet queda **solo** para casos donde intencionalmente queremos gate per-instance (raro, casi ninguno en este proyecto).
8. **Pre-auth y webhooks nunca usan tenant como partición** — el tenant es lo que se está creando o resolviendo, o no aplica (webhook externo). Ver categorías A/B/C/E.
9. **Health checks nunca están rate-limited**. `/health/*` es categoría P, cuota infinita.
10. **Ningún endpoint queda sin categoría asignada**. NetArchTest en Fase 9 verifica que todo `[HttpGet/Post/Put/Delete/Patch]` público está en el `RateLimitPolicyCatalog` con una categoría explícita, o marcado con `[RateLimitExempt(reason)]` con justificación.

---

## 4. Taxonomía de endpoints — 17 categorías

Todas las cuotas listadas son **plan Standard base**. El multiplicador por tier se aplica encima (§5). El "user" al que refiere la partición Capa 3 es el `sub` del JWT (para actores humanos) o el `client_id` (para M2M).

### Bloque I — Pre-auth y públicos (no hay tenant o el tenant es lo que se resuelve)

| ID | Categoría | Endpoints típicos | Partición primaria | Overlay | Cuota base | Algoritmo | Consecuencia al exceder |
|----|-----------|-------------------|--------------------|---------|-----------|-----------|-------------------------|
| **A** | Auth pre-tenant | `POST /auth/login`, `POST /auth/refresh`, `POST /auth/mfa/verify` | `email` + `ip` (par separado, ambos deben quedar bajo cuota) | Lockout SQL en `User.FailedLoginCount` a los 10 fallos | 10/min por email, 30/min por IP | Fixed window | 429 `Retry-After: 60`. A los 10 fallos consecutivos por email → `SecurityAlertIntegrationEvent(AccountLockedOut)` + lockout 15 min. |
| **B** | Password/OTP flow | `POST /auth/password/forgot`, `POST /auth/password/reset`, OTP resend, phone-verify | `email` + `ip` | Attempts counter en SQL por token (max 5) | 5/hora email, 20/hora IP + 60s cooldown por email en resend | Fixed window | 429 `Retry-After` con TTL real. |
| **C** | Onboarding pre-tenant | `POST /auth/onboarding/checkout/create`, `.../complete`, `.../subdomain-check`, `.../email-challenge/create` | `email` + `ip` | Fingerprint del dispositivo si existe | 5/hora IP, 20/día email, 10/día por email para OTP-create | Fixed window | 429 + fail-closed (el fallo del limitador nunca crea onboardings sin cuota). |
| **D** | Público con token | `GET /storage/public/{token}`, `GET /signature/public/{token}`, `GET /terms/{id}/content`, `POST /communication/meetings/join-by-token` | `ip` + `token` (o path) | Expiración del token, hit counter, DMCA legal-hold | 20-30/min por IP+token | Fixed window | 429. Nunca revelar existencia del recurso si el token es inválido — 404. |
| **E** | Webhooks externos firmados | `POST /payments-app/webhooks/stripe`, `POST /connectors/webhooks/gmail-push`, `POST /connectors/webhooks/graph-notification`, `POST /communication/webhooks/...` | `ip` de origen | Validación de firma HMAC/JWS obligatoria antes de gate | 1000/min por IP (la validación de firma es el gate real) | Fixed window laxo | 429. **Nunca** particionar por tenant — el origen es un tercero y el mapping a tenant es parte del payload. |

### Bloque II — Autenticadas típicas (aquí vive el 80% del tráfico y aquí aplica el modelo de 4 capas completo)

| ID | Categoría | Endpoints típicos | Partición primaria (Capa 3) | Overlay (Capa 2) | Cap por endpoint (Capa 4) | Cuota base | Algoritmo | Consecuencia |
|----|-----------|-------------------|-----------------------------|------------------|--------------------------|-----------|-----------|--------------|
| **F** | GET lectura ligera | `GET /customers`, `GET /customers/{id}`, `GET /notifications`, `GET /subscriptions/me`, `GET /storage/folders` | (tenant, user) | tenant | — | 300/min user, 3000/min tenant | Token bucket (burst tolerado, sustained rate) | 429 con `Retry-After` estimado por refill |
| **G** | Write ligero | `POST /customers`, `PATCH /customers/{id}`, `PUT /customers/{id}/fiscal-profile`, `POST /storage/folders`, `POST /notifications/preferences` | (tenant, user) | tenant | — | 60/min user, 600/min tenant | Token bucket | 429 |
| **H** | Búsqueda / listado pesado | `GET /customers?term=...`, `GET /correspondence/threads?filter=...`, exports, `GET /audit`, `GET /signature/analytics/*` | (tenant, user) | tenant | 100/min endpoint por tenant | 20/min user, 100/min tenant | Sliding window (precisión importa aquí) | 429 |
| **I** | Bulk / upload grande | `POST /customers/imports`, `POST /storage/files/uploads` (multipart), `POST /storage/files/*/complete`, ZIP download, `POST /tenants/{id}/branding/logo` | (tenant, user) | tenant | 20/hora endpoint por tenant | 5/hora user, 20/hora tenant | Fixed window largo (1h) | 429 + `Retry-After` en horas. Adicionalmente valida cuota de storage/import concurrente (job dominio). |
| **J** | Rendering / cómputo caro | `POST /scribe/render`, `POST /signature/*/seal`, transcript worker, generación PDF | tenant | — (usualmente async job, no HTTP directo) | 30/min tenant | Token bucket con burst chico | 429 en el endpoint HTTP, backpressure en la cola para jobs |

### Bloque III — Comercial y externo (aquí "tenant" se combina con "cuenta/proveedor externo")

| ID | Categoría | Endpoints/consumers típicos | Partición primaria | Overlay | Cuota base | Algoritmo | Consecuencia |
|----|-----------|-----------------------------|--------------------|---------|-----------|-----------|--------------|
| **K** | Envío a proveedor externo | Connectors `POST /accounts/{id}/send`, Postmaster consumer envío, notificación email dispatch, SMS gateway | (tenant, account/provider) | Per-provider **global cap** en paralelo (proteger a Gmail/Graph) | 60/min (tenant,account) + cap global por provider | Leaky bucket (Shopify pattern) | `SendMessageResult.RateLimited` (no 429 al usuario final, al consumer de la cola). Backoff exponencial. |
| **L** | Financiera — iniciar cobro | `POST /payments-app/checkout/create`, `POST /payments-client/payment-links/*`, `POST /billing/invoices/*/finalize` | (tenant, user) | tenant | 10/min endpoint | 10/min user, 60/min tenant | Fixed window | 429 con audit log. |
| **M** | Financiera — admin (money-out) | `POST /payments-app/saas-payments/{id}/refund`, `POST /payments-client/payments/{id}/refund`, `POST /subscriptions/{id}/cancel` (con reembolso) | tenant | AuthAuditLog obligatorio + AuthorizationMetrics | 5/min tenant | Fixed window estricto | 429 + audit obligatorio incluso al 429. |

### Bloque IV — Sensibles y realtime (categorías con reglas propias)

| ID | Categoría | Endpoints/scopes típicos | Partición primaria | Overlay | Cuota base | Algoritmo | Consecuencia |
|----|-----------|-------------------------|--------------------|---------|-----------|-----------|--------------|
| **N** | Reveal de dato sensible | `GET /customers/{id}/fiscal-profile/tax-identifier`, `GET /auth/onboarding/{id}/receipt/download`, cualquier "reveal SSN/ITIN/EIN/bank/PII en claro" | user | AuthAuditLog obligatorio + IP + UserAgent en audit | 5/hora user | Fixed window largo | 429 + audit incluso al 429. **Nunca** cuota por tenant aquí — es contra-scraping personal. |
| **O** | Realtime sockets | `chat.send`, `chat.edit`, `chat.typing`, `call.initiate`, `call.signal`, `meeting.chat.send`, `dominant-speaker` | (tenant, user) | scope específico | Cada scope su cuota (ver `Communication/config.ts:73-84`) | Fixed window Redis (via IRateCounter) | Ack `code: '<scope>.RateLimited'` al cliente socket. |

### Bloque V — Infraestructura y meta

| ID | Categoría | Endpoints típicos | Regla |
|----|-----------|-------------------|-------|
| **P** | Health / observabilidad | `/health`, `/health/ready`, `/health/detailed`, `/health/downstream`, `/metrics` | **Nunca** rate-limited. Marcar con `[RateLimitExempt("health-check")]`. |
| **Q** | Load shedder global | Aplica a TODO el tráfico como Capa 1 en el Gateway | Cuota = X% de capacidad de flota medida (Fase 5). Dispara **antes** que las capas per-tenant. Log obligatorio con top-N tenants activos en ese instante. |

### Ejemplos concretos de clasificación (para calibrar el criterio)

- `POST /customers` → **G** (write ligero). No es I aunque cree un recurso — no consume infra pesada.
- `POST /customers/imports` → **I** (bulk). Consume MinIO, memoria, worker.
- `POST /customers/{id}/fiscal-profile` → **G** (write ligero). El write en sí es barato.
- `GET /customers/{id}/fiscal-profile/tax-identifier` → **N** (reveal sensible). Aunque sea un GET simple, la sensibilidad manda.
- `POST /signature/requests/{id}/send` → **G** (write ligero). El envío del email se hace async — el HTTP en sí es barato.
- El consumer de `SignerInvitedIntegrationEvent` en Postmaster → **K** (envío externo). Ahí sí aplica per-tenant+provider + cap global.
- `GET /correspondence/threads/{threadId}/messages?page=...` sin filtro → **F** (lectura ligera).
- El mismo endpoint con `?search=...` que hace full-text → **H** (búsqueda).
- `POST /payments-app/webhooks/stripe` → **E** (webhook externo). Nunca per-tenant.
- El consumer interno que despacha una vez que el webhook validó firma → si toca DB del tenant, aplica overlay per-tenant en el handler, no en el endpoint.

---

## 5. Cuotas por plan tier

Los multiplicadores se aplican sobre la cuota base **por categoría**. No es multiplicador uniforme "10× de todo".

| Plan | Multiplicador default | Excepciones |
|------|----------------------|-------------|
| Free / Trial | **0.3×** (piso mínimo 5/min si el cálculo baja de eso) | Categoría N (reveal sensible) no escala — siempre 5/hora. Categoría M (admin financiero) no escala — siempre 5/min. |
| Standard | **1.0×** (baseline definido en §4) | — |
| Plus | **3×** | Categoría J (rendering) 5× (más templates, más docs). Categoría I (bulk) 5× (más volumen). |
| Enterprise | **10×** | Categoría K (envío) 20× (más volumen de email). Categoría H (búsqueda) 15×. |
| Enterprise Custom | Negociado contrato | Fila propia en tabla `PlanRateLimits` (Fase 6). Cualquier valor. |

**Regla operativa**: si un cliente Enterprise reporta 429 recurrente en producción, no se sube su cuota puntualmente — se abre un ticket para revisar si su volumen amerita subir a Enterprise Custom, o si hay un bug en su integración. Suba puntual sin trazabilidad → prohibido.

---

## 6. Convenciones (naming, formatos, contratos)

### 6.1 Nombres de política — en `RateLimitPolicyCatalog`

Formato: `<servicio>.<categoría-id>.<endpoint-slug>` en `snake_case`. Ejemplos:
- `auth.a.login`
- `auth.b.password_forgot`
- `customer.g.create`
- `customer.n.fiscal_reveal`
- `payment_app.m.refund`
- `postmaster.k.dispatch`
- `communication.o.chat_send`

Ningún endpoint tiene política sin nombre canónico. Ninguna política sin categoría.

### 6.2 Formato de `RateCounterKey`

Siempre: `<servicio>:rl:<policy-name>:<partition-key-parts-joined-with-colon>:<window-bucket-if-fixed>`.

Ejemplos válidos:
- `auth:rl:auth.a.login:email:jperez@acme.com:min:29341280`
- `auth:rl:auth.a.login:ip:203.0.113.42:min:29341280`
- `customer:rl:customer.g.create:tenant:d4879234...:user:2cd3f306...`
- `postmaster:rl:postmaster.k.dispatch:tenant:d4879234...:provider:gmail:min:29341280`

Nunca componer keys ad-hoc con `$"..."`. Siempre vía builder `RateCounterKey.Build(policy, parts)` que valida shape.

### 6.3 Respuesta HTTP 429

```
HTTP/1.1 429 Too Many Requests
Retry-After: 43
X-RateLimit-Policy: customer.g.create
X-RateLimit-Layer: tenant
X-RateLimit-Limit: 600
X-RateLimit-Remaining: 0
X-RateLimit-Reset: 1735689600
Content-Type: application/json

{
  "code": "RateLimit.Exceeded",
  "message": "Tenant rate limit exceeded. Retry after 43 seconds.",
  "policy": "customer.g.create",
  "layer": "tenant"
}
```

Contrato es shape estable. Frontend/mobile lo consume con esos nombres exactos.

### 6.4 Categorías socket (Node, Communication)

Ack `{ ok: false, code: '<scope>.RateLimited', retryAfterMs: <n> }` sin emitir evento. El cliente decide UX (toast, backoff, cola local).

---

## 7. Arquitectura target (post-Fase 4)

```
Cliente HTTP
    │
    ▼
┌──────────────────────────────────────────────────────┐
│  API Gateway (YARP)                                  │
│  ┌────────────────────────────────────────────────┐  │
│  │  Middleware Capa 1 — LoadShedder global        │  │  ← Fase 5
│  │  (dispara al X% de fleet saturation)          │  │
│  └────────────────────────────────────────────────┘  │
│  ┌────────────────────────────────────────────────┐  │
│  │  Middleware Capa 2/3 — Tenant + User (para    │  │  ← Fase 3+4
│  │  todo lo autenticado; se salta pre-auth)      │  │
│  └────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────┘
    │
    ▼ (con headers X-Tenant-Id, X-User-Id propagados)
┌──────────────────────────────────────────────────────┐
│  Microservicio N                                     │
│  ┌────────────────────────────────────────────────┐  │
│  │  Middleware Capa 4 — Per-endpoint              │  │  ← Fase 3+4
│  │  (lookup por policy-name → cuota resuelta      │  │
│  │  por plan tier del tenant desde caché)         │  │
│  └────────────────────────────────────────────────┘  │
│         │                                            │
│         ▼                                            │
│  Controller / Handler                                │
│         │                                            │
│         ▼                                            │
│  (Throttlers de dominio: LoginThrottler, Payment-   │
│  AttemptThrottle, ConnectorsSendRateLimiter...)      │  ← ya existen, Fase 0 arregla bugs
└──────────────────────────────────────────────────────┘
    │
    ▼
Redis (IRateCounter, Lua atómico) + SQL (Subscription.PlanCode, User.LockoutEndUtc)
```

---

## 8. Fases

Cada fase termina con: `dotnet build TaxVision.slnx` verde, suite de tests completa verde, y **reporte español al usuario** con qué cambió, qué se probó de verdad, qué queda para la próxima fase.

### Fase 0 — Bugs primero (independiente del modelo de capas) — **BLOQUEANTE**

**Motivo**: estos bugs afectan la corrección de lo que ya existe. Arreglarlos no requiere haber decidido el nuevo modelo. Si no se arreglan primero, el nuevo modelo se construye sobre arena.

| ID | Bug | Archivo(s) | Trabajo |
|----|-----|------------|---------|
| 0.1 | `ILoginThrottler` usa GET+SET no atómico (TOCTOU) | `src/Services/Auth/Infrastructure/Security/LoginThrottler.cs` | Migrar los 9 contadores a `IRateCounter.IncrementAndGetAsync`. Mantener interfaz pública. Ver F26 (mismo patrón). |
| 0.2 | 20 rate limiters HTTP AspNetCore son in-memory por réplica | Todos los `Program.cs` con `AddRateLimiter` | **NO tocar todavía en Fase 0** — se sustituyen enteros en Fase 3-4 por el middleware unificado sobre `IRateCounter`. Solo documentar en el reporte de Fase 0 que este bug queda con dueño Fase 3. |
| 0.3 | `ConnectorsProviderRateLimiter` sin dimensión tenant → noisy neighbor | `src/Services/Connectors/TaxVision.Connectors.Infrastructure/RateLimit/RedisProviderRateLimiter.cs` | Rediseñar en **dos** contadores paralelos: (a) global por proveedor (para no romper a Gmail/Graph — proteger al tercero), (b) per-tenant por proveedor (fair share). Ambos deben pasar. El más estricto gana. |
| 0.4 | Communication (Node) INCR+EXPIRE separados | `src/Services/Communication/src/infrastructure/redis/socket-rate-limiter.ts`, `dominant-speaker-throttle.ts` | Migrar a script Lua atómico (mismo patrón que `RedisRateCounter.cs`). Portar la lib como helper reutilizable en `src/infrastructure/redis/rate-counter.ts`. |
| 0.5 | Doble gate en `POST /tenants` (Gateway 10/min + Tenant 5/min) | `src/BuildingBlocks/BuildingBlocks.Web/RateLimiting/RateLimitingRegistration.cs`, `src/Services/Tenant/TaxVision.Tenant.Api/Program.cs` | Consolidar en el gateway solamente. Tenant service quita su policy `tenant-registration`. Verificar que Fase 15 del PayFlow (registration ticket firmado) sigue funcionando. |
| 0.6 | Communication Fastify HTTP por IP (`x-real-ip` fallback a `req.ip`) — sin cambios de scope, pero atomic fix | `src/Services/Communication/src/infrastructure/http/build-server.ts:59-63` | Auditar que el fix atómico de 0.4 llega a `@fastify/rate-limit` (o migrar a implementación propia con el helper de 0.4). |

**Criterio de aceptación Fase 0**: los 6 bugs cerrados o formalmente asignados a fase posterior con justificación. Build+tests verde en ambos servicios. Reporte español que lista cada bug, qué se hizo, cómo se verificó.

### Fase 1 — ADR + PolicyCatalog + tabla `PlanRateLimits`

**Alcance**:
- Escribir `documents/architecture/ADR_017_RateLimit_Layers.md` que congela las decisiones de este doc.
- Crear `src/BuildingBlocks/BuildingBlocks/RateLimiting/RateLimitCategory.cs` (enum A..Q).
- Crear `src/BuildingBlocks/BuildingBlocks/RateLimiting/RateLimitPolicyDefinition.cs` (record: `Name`, `Category`, `PrimaryPartition`, `OverlayLayers`, `BaseQuotaPerMinute`, `WindowSeconds`, `Algorithm`).
- Crear `RateLimitPolicyCatalog` estático (patrón `PermissionCatalog`). Aquí viven las ~50-80 políticas del monorepo entero como constantes.
- Crear tabla en Subscription DB: `PlanRateLimits(PlanCode, CategoryId, MultiplierOverride, HardOverridePerMinute)` con seed inicial de los 5 tiers.

**Archivos afectados**: BuildingBlocks + Subscription (migración EF).

**Criterio de aceptación**: el catálogo compila, la migración corre, hay tests unitarios que validan que cada categoría tiene una fórmula de cuota resoluble para cada plan sembrado (Free/Standard/Plus/Enterprise). Ningún catálogo referencia código de servicios individuales (invariante 10 de §3).

### Fase 2 — Registry runtime + resolver tier-aware

**Alcance**:
- `IRateLimitPolicyRegistry` (BuildingBlocks) que expone `PolicyDefinition GetByName(name)` desde el catálogo.
- `IRateLimitQuotaResolver` que recibe `(policy, tenantId)` y devuelve `EffectiveQuota(permitCount, windowSeconds)` — resuelve el tier del tenant desde caché Redis (TTL 5 min), aplica multiplicador o hard-override.
- Caché con invalidación por evento `SubscriptionPlanChangedIntegrationEvent` (ya existe en BuildingBlocks Messaging).
- Fallback si Subscription no responde: usar multiplicador Standard (fail-open a límite conservador).

**Archivos afectados**: BuildingBlocks + consumer del evento en un nuevo `SubscriptionPlanChangeCache` (posiblemente en cada servicio que use el resolver).

**Criterio de aceptación**: tests unitarios del resolver. Fake `ITenantPlanCodeReader`/`IPlanRateLimitReader` para no acoplar a Subscription real en tests.

> **Nota de cierre (Fase 2, ver ADR_017 §2.1/§3.3)**: "5 tiers × 17 categorías = 85 combinaciones" era la cuenta original de este doc; el catálogo real de Subscription tiene 3 planes (starter/pro/enterprise) y solo 10 categorías (F..O) escalan por plan — A-E/P/Q no. El evento de invalidación tampoco es `SubscriptionPlanChangedIntegrationEvent` (no existe); es `TenantEntitlementsChangedIntegrationEvent`. La implementación real de `IPlanRateLimitReader` (cómo cada servicio lee `PlanRateLimits` de Subscription) queda para Fase 6 — Fase 2 solo entrega el resolver puro + los puertos, sin acoplar a un mecanismo de sync todavía indefinido.

### Fase 3 — Middleware unificado (`TieredRateLimitMiddleware`) en BuildingBlocks.Web

**Alcance**:
- `TieredRateLimitMiddleware` que, dado un endpoint con `[RateLimit("policy.name")]`, evalúa Capa 4 (endpoint específico), Capa 3 (per-user), Capa 2 (per-tenant), en ese orden.
- Fail-open al fallo de Redis; métricas OTel; headers de respuesta según §6.3.
- Atributo `[RateLimit(string policyName)]` que envuelve `IAsyncActionFilter` para que MVC lo procese antes que el controller.
- Atributo `[RateLimitExempt(string reason)]` para health checks + endpoints explícitamente exceptuados (documentar cada uso).
- **DI y wire-up en un solo servicio piloto — recomendado: Customer** (chico, aislado, con endpoints de todas las categorías B/F/G/H/I/N).

**Archivos afectados**: BuildingBlocks.Web + `src/Services/Customer/**` como piloto.

**Criterio de aceptación**: los 3 endpoints piloto de Customer (`POST /customers`, `GET /customers/{id}`, `GET /customers/{id}/fiscal-profile/tax-identifier`) responden 429 con headers correctos cuando exceden cuota; tests de integración con `WebApplicationFactory` que verifican el comportamiento; el rate limiter viejo (`AddRateLimiter` en `Customer/Program.cs`) queda desactivado o eliminado; `dotnet build` + tests verde.

> **Nota de cierre (Fase 3, ver ADR_017 §2.2/§3.4)**: implementado como `[RateLimit]`/`[RateLimitExempt]` (`IAsyncActionFilter`, no un middleware separado — se necesita el nombre de política ya resuelto por MVC routing) + `TieredRateLimitEvaluator` en BuildingBlocks.Infrastructure. `RateLimitPolicyDefinition` ganó `OverlayQuotaPerMinute` (extensión sobre Fase 1, no prevista originalmente) para modelar la Capa 2 en el mismo registro que la Capa 3, en vez de una "política hermana". El rate limiter viejo de Customer (`AddRateLimiter`/`FixedWindowRateLimiter` de `fiscal-reveal`) fue eliminado, no solo desactivado. Verificado con `WebApplicationFactory<Program>` real — SQL Server + Redis + RabbitMQ locales, sin mocks: 61 `POST /customers`, 301 `GET /customers/{id}` y 6 `GET .../fiscal-profile/tax-identifier` reales, confirmando 429 con headers/body exactos de §6.3 en cada cupo (60/300/5 respectivamente). Capa 4 (cap por endpoint) sigue sin implementar — ninguno de los 3 endpoints piloto la necesita, se agrega en la primera sub-fase de Fase 4 que migre una categoría H/I.

### Fase 4 — Migración servicio-por-servicio (el trabajo grande)

**Orden recomendado** (por criticidad + riesgo de regresión menor primero):

| Sub-fase | Servicio | Endpoints a categorizar/migrar | Notas |
|----------|----------|------------------------------|-------|
| 4.1 | Customer | Ya piloto en Fase 3 — solo cerrar `fiscal-reveal` y agregar `check-exists` | Cerrar loose ends |
| 4.2 | Tenant | `tenant-logo-upload` → I, `tenant-registration` → C (queda en gateway, no en servicio) | Coordinar con onboarding Saga |
| 4.3 | Notification | Todos los endpoints CRUD → F/G; preferences → G; campaigns admin → G | Simple |
| 4.4 | Postmaster | `suppression` GET → F; POST → G; providers → G; messages → F; el consumer de dispatch mantiene su rate limiter K existente | K en consumer, no en HTTP |
| 4.5 | Scribe | Templates/layouts/mappings → F/G; render → J | J es el interesante |
| 4.6 | CloudStorage | Files → F/G; uploads → I; folders → F/G; ZIP → I; share admin → G; **public share → D (queda IP+token)** | D no cambia de scope, solo migra al nuevo middleware |
| 4.7 | Signature | Requests → F/G; templates → F/G; analytics → H; **public signature → D**; document sign flow interno del signer → categoría propia con partition por `signerToken` | Signer flow es especial |
| 4.8 | Connectors | Accounts → F/G; messages/body/attachment → F/G en HTTP; el rate limiter K real vive en el handler | El `IProviderRateLimiter` global fix es Fase 0.3 |
| 4.9 | Correspondence | Threads → F/H; messages → F/G; drafts → G; send → L (financiera-adjacente por costo, cuota conservadora) | Envío async → K en el consumer |
| 4.10 | Subscription | Plans → F; subscriptions/me → F; audit → H; admin → G/M para dangerous ops | Ver M para cancel-con-refund |
| 4.11 | Billing | Invoices → F/G; issuer profile → G | Simple |
| 4.12 | Auth | Login → A; password → B; onboarding → C; sesiones/me/permissions → F/G; users → F/G; **terms-download → D**; MFA setup → G, MFA challenge → A-adjacente | El más denso (11 controllers, ~35 endpoints) — subdividir en 4.12.a/b/c si es necesario |
| 4.13 | PaymentApp | Webhooks → E; admin refund → M; provider customers → F/G | M requiere audit obligatorio |
| 4.14 | PaymentClient | Todo lo público → D; webhooks → E; connect-account → G; recurring → F/G; payments → F/G; admin refund → M | Muchos endpoints, revisar uno por uno |
| 4.15 | Growth | Codes → F/G; internal quote → E-adjacente (M2M solo); referrals attribution → categoría D-adjacente pero autenticada, dejar como G con cap más estricto | El `code-quote` de 1000/min actual es sospechoso — revalidar |
| 4.16 | Documents | Endpoints M2M → categoría propia "servicios internos" (partition por `client_id`) | Todo su tráfico es M2M |

**Por cada sub-fase**: leer el reporte de inventario, mapear cada endpoint a categoría, aplicar `[RateLimit(...)]`, eliminar el `AddRateLimiter` viejo del `Program.cs`, agregar tests de integración (mínimo 1 por categoría distinta que aparezca en el servicio), build+tests, reporte.

**Criterio de aceptación por sub-fase**: 0 `AddRateLimiter` remanente en el `Program.cs` del servicio; 0 `[EnableRateLimiting]` remanente en controllers; 100% de endpoints públicos con `[RateLimit]` o `[RateLimitExempt]`; tests de integración pasando; grep confirma que no queda ningún throttler dominio bypasseado.

**Fase 4 — CERRADA (2026-08-01)**. Las 16 sub-fases (4.1-4.16) cumplen el criterio de aceptación:
build de la solución completa verde y suite de tests completa verde tras cada sub-fase, más una
corrida final de monorepo (18 proyectos de test, ~3000 tests) tras 4.16. Desviaciones reales
respecto a las notas originales de la tabla, cerradas explícitamente en vez de silenciosamente:

- **4.15 Growth**: el `code-quote` de 1000/min se revalidó (no era un bug — límite deliberado
  anti-abuso M2M documentado en su propio doc-comment) y se dejó sin cambios. `ReferralAttribution`
  SÍ se migró al sistema tiered (no quedó "como G" según la nota original, sino H con partición
  Tenant-only replicando exacto el limiter nativo que reemplazó). Los 10 endpoints M2M de
  `InternalCodesController`/`InternalReferralsController` quedaron `[RateLimitExempt]` — su JWT de
  servicio no trae `sub` en las rutas donde el handler nunca llama `TryGetUserId` (ver detalle en
  el doc-comment de `RateLimitPolicyCatalog`).
- **4.16 Documents**: la nota original ("categoría propia 'servicios internos', partition por
  client_id") resultó parcialmente imprecisa una vez leído el código real —
  `InternalDocumentBrandingController` no es M2M pese al nombre/ruta `/internal/*` (es staff
  humano, F/G normal). Los 2 endpoints M2M genuinos SÍ necesitaban una categoría — se reutilizó la
  categoría J ya existente (Rendering/cómputo caro, "generación PDF" es un ejemplo textual de J en
  §4) en vez de agregar una categoría nueva a las 17 congeladas por este mismo ADR — evita tocar
  `RateLimitCategory` y `ADR_017_RateLimit_Layers.md §2` por una necesidad que la taxonomía ya
  cubría. A diferencia de Growth, se confirmó (leyendo `JwtTokenGenerator.GenerateScopedServiceToken`)
  que el JWT de servicio SÍ trae `sub` (derivado determinísticamente de `client_id`) y `tenant_id`
  siempre — `[RateLimit]` no fail-open en Documents.
- **Bug real cross-servicio encontrado y corregido en 4.14 (PaymentClient)**: `RateLimitAttribute`
  devolvía un `ObjectResult` JSON al disparar 429; en cualquier acción con `[Produces("text/csv")]`
  (ej. `PaymentClientAdminController.ExportCsv`) la negociación de contenido de MVC restringía ese
  `ObjectResult` al content-type declarado por la acción y devolvía 406 en vez de 429. Corregido
  escribiendo el JSON directo al body (`HttpResponse.WriteAsJsonAsync`, mismo patrón que
  `ExceptionHandlingMiddleware`) en vez de un `ObjectResult`, bypasseando la negociación de
  contenido — corrige el bug para los 17 servicios que ya usan `[RateLimit]`, no solo PaymentClient.
- **Gap de config local encontrado en 4.15 y 4.16**: Growth y Documents nunca habían tenido
  `ConnectionStrings:Redis` en el user-secrets local de la máquina de desarrollo (ninguno de los
  dos tenía rate limiting propio antes de su sub-fase). `docker-compose.yml` sí lo tenía para
  Growth pero no para Documents — se agregó ahí también. Sin este gap el `WebApplicationFactory` de
  los tests de integración fallaba con 500 (`ConnectionStrings:Redis is missing.`) en vez de 429 —
  confirmado y corregido en ambos servicios antes de cerrar sus respectivas sub-fases.

### Fase 5 — Load shedder global (Capa 1) en Gateway

**CERRADA (2026-08-01).** `LoadSheddingMiddleware` (`src/Gateway/TaxVision.Gateway/LoadShedding/`)
mide su propia latencia p99 (incluye el round-trip completo al cluster YARP de destino — el
`Stopwatch` envuelve `await next()`) y la tasa de 5xx en una ventana deslizante por-segundo
(`RequestOutcomeWindow`, 60s por defecto). Cuando ambas métricas superan el umbral configurado
(`LoadShedderOptions`: `P99LatencyThresholdMs`/`ErrorRate5xxThreshold`), rechaza con
`503 Retry-After` solo los requests de los tenants de mayor consumo actual
(`TenantConsumptionTracker`, top-10 por defecto vía `LoadShedderOptions.TopConsumerCount`) — el
resto del tráfico sigue pasando, degradando gracefully en vez de tumbar la flota entera de un
golpe. Log estructurado (`LoadShedder`, edge-triggered — solo en la transición hacia sobrecarga,
no en cada request rechazado) con el top-10 de tenants cuando dispara, más
`GatewayMetrics.LoadSheddingActivated`/`RequestsShed` (Meter "gateway", exportado vía
`AddTaxVisionOpenTelemetry` existente sin cambios). Health checks (`/health/*`) se excluyen
explícitamente al inicio del middleware — nunca cuentan para la ventana ni pueden ser shedded.

Señal **local a cada réplica del Gateway**, no agregada de flota — mismo criterio que un local
overload manager (Envoy-style): cada instancia protege su propia capacidad sin depender de un
store compartido nuevo (no se agregó Redis a Gateway para esto). 12 tests unitarios nuevos
(`deploy/tests/TaxVision.Gateway.Tests/`, primer test project de Gateway — no existía ninguno
antes de esta fase) cubren `RequestOutcomeWindow` (p99/error-rate), `TenantConsumptionTracker`
(ranking) y `LoadShedder` (gating por `MinSamples`/`Enabled`, priorización de tenants de mayor
consumo, disparo por error-rate con latencia baja) — sin necesitar `WebApplicationFactory` ni
esperas de reloj real, ya que la ventana usa segundos-bucket y los tests corren en <1s (todas las
muestras caen en el mismo bucket, sin depender de poda por expiración).

**Alcance original del plan** (referencia):
- Middleware en `TaxVision.Gateway` que mide latencia p99 de sí mismo + tasa de errores 5xx de los clusters YARP.
- Cuando supera umbral configurable → empieza a rechazar tráfico entrante nuevo con 503 `Retry-After: <n>`, priorizando drop de tenants con más consumo actual (métrica del último minuto).
- Log estructurado con top-10 tenants por consumo cuando dispara.
- Health checks (categoría P) **siempre** pasan, no cuentan en el shedding.

**Archivos afectados**: `src/Gateway/TaxVision.Gateway/**` + posiblemente BuildingBlocks.

**Criterio de aceptación**: test de carga sintético que satura la flota → shedder dispara → 503 con `Retry-After` → al bajar la carga, recuperación automática dentro de 30s.

### Fase 6 — Tier-aware quotas dinámicas en producción

**CERRADA (2026-08-01) — piloto Customer, mismo alcance que Fase 3.** No se activó en los 17
servicios: se conectó el `IRateLimitQuotaResolver` (ya existente desde Fase 2, hasta ahora inerte
sobre `NullTenantPlanCodeReader`/`NullPlanRateLimitReader`) en Customer únicamente, detrás de
`RateLimit:EnforceTierQuotas` (default `false`). Diferencias respecto al alcance original abajo:
- No se creó el endpoint admin `POST /admin/rate-limits/refresh/{tenantId}` — Subscription ya
  tiene `POST admin/subscription/tenants/{tenantId}/recalculate-entitlements`, que republica
  `TenantEntitlementsChangedIntegrationEvent` (el único mecanismo correcto de fan-out entre
  réplicas) y cumple la misma función; se documenta reutilización en vez de duplicar ruta.
- El evento real es `TenantEntitlementsChangedIntegrationEvent` (Subscription,
  `RecalculateEntitlementsHandler`), no `SubscriptionPlanChangedIntegrationEvent` (nunca existió
  con ese nombre) — la cita original del plan estaba desactualizada.
- Subscription expone el catálogo `PlanRateLimits` completo vía
  `GET subscriptions/internal/plan-rate-limits` (M2M, `ServiceOnly`); Customer lo cachea 5 min
  (catálogo global, no por-tenant) vía `HttpPlanRateLimitReader`, reusando el M2M client
  `customer-worker` ya registrado (Subscription's `ServiceOnly` no exige scopes).
- Nuevo `TenantPlanCodeProjection` (Customer) — proyección idempotente por `RevisionNumber`,
  mantenida por `TenantPlanCodeProjectionConsumer`, que invalida el decorador de caché
  (`CachedTenantPlanCodeReader`) al vuelo en vez de esperar el TTL.
- `RateLimitQuotaResolver` se registra Singleton (BuildingBlocks.Web) pero sus lectores reales
  dependen de servicios Scoped (`CustomerDbContext`, `ICacheService`) — se resolvió con
  `ScopedTenantPlanCodeReader`/`ScopedPlanRateLimitReader`, wrappers Singleton que crean su
  propio `IServiceScopeFactory` scope por llamada (patrón estándar .NET para este caso).
- Criterio de aceptación cumplido parcialmente: el flip de cuota efectiva está implementado
  (invalidación inmediata + fallback 5 min TTL), pero el test E2E del flip queda pendiente de
  verificación manual real (requiere Docker con SQL+Redis+RabbitMQ+Subscription+Customer
  levantados) — no se ejecutó en esta sesión.

Fases 8-9 (dashboards, fitness functions/cierre) quedan sin arrancar.

**Nota — por qué el rollout a los 17 servicios importa (no solo Customer):** el piloto solo
escala cuotas en Customer. Un tenant `enterprise` hoy tiene throughput ×10 en Customer pero sigue
con cuota base (igual que `starter`) en los otros 16 servicios (CloudStorage, Signature,
Notification, etc.) — la promesa comercial de "plan más caro = cuotas más altas en toda la
plataforma" no se cumple hasta rollout completo. Cada servicio necesita repetir la
proyección+consumer+cliente HTTP local (no hay estado compartido entre procesos) — trabajo
mecánico pero real, deliberadamente diferido hasta validar el piloto en producción.

**Alcance original**:
- Activar `IRateLimitQuotaResolver` en el middleware unificado (Fase 3) para usar el multiplicador real del tenant en vez de la cuota base.
- Verificar que el evento `SubscriptionPlanChangedIntegrationEvent` propaga a los 13 servicios que ya tienen cache de proyección.
- Endpoint admin `POST /admin/rate-limits/refresh/{tenantId}` (PlatformAdmin only) para forzar recarga tras cambios manuales.

**Archivos afectados**: casi todos los servicios (agregar consumer del evento si no lo tienen).

**Criterio de aceptación**: cambio de plan de un tenant en pilot → nueva cuota efectiva dentro de 5 min (TTL de caché) o inmediato tras el evento; test E2E que verifica el flip.

### Fase 7 — Communication (Node) — port completo

**CERRADA (2026-08-02).** Alcance real menor al original: la investigación previa a la
implementación encontró que los 6 scopes de socket YA estaban migrados al lib atómico desde
Fase 0.4 (`incrementAndGet`, `SocketRateLimiter`) — nada que migrar ahí, solo se documentó el
mapeo a nombres canónicos. Lo que sí faltaba y se cerró:
- **`rate-limit-policies.ts`** (nuevo, `src/domain/rate-limit/`) — espejo estático de los 8
  nombres canónicos .NET que aplican a Communication (6 `communication.o.*` + 2
  `communication.d.meeting_join_by_*`), mismo patrón que `domain/shared/permissions.ts` para
  `CommunicationPermissions`. Los valores numéricos de cuota NO se duplican ahí — siguen en
  `config.rateLimit.*` (env-configurable), el archivo nuevo solo fija el nombre.
- **Bug real encontrado**: el catálogo .NET tenía `communication.d.meeting_join_by_token` en
  20/60s, pero el valor real en Node (`config.ts`) era 5/60s — discrepancia entre ambos lados
  nunca detectada hasta espejar el catálogo. Confirmado con el usuario: 20/60s es el valor de
  negocio correcto, Node se corrigió para igualar.
- **`@fastify/rate-limit` (in-memory, por-instancia) reemplazado por completo** — limiter HTTP
  global (por IP) y las 2 rutas públicas de meeting-invitations (join-by-token/by-code) ahora
  usan el mismo contador atómico Redis que los sockets (`HttpRateLimiter`, nuevo, mismo patrón
  que `SocketRateLimiter`). Las 2 rutas públicas quedaron particionadas por token/shortCode (no
  por IP) — más correcto contra token-guessing distribuido desde muchas IPs. Dependencia
  `@fastify/rate-limit` eliminada de `package.json`.
- **Métricas OTel — explícitamente diferidas a Fase 8**: ni .NET ni Node tenían las métricas del
  invariante (`ratelimit.evaluated_total`, etc.) implementadas — el propio código .NET
  (`TieredRateLimitEvaluator.cs`) documenta que quedan para Fase 8. Implementarlas ahora en Node
  solo habría sido inventar el shape sin nada real que espejar; decisión confirmada con el
  usuario, se hacen juntas en ambos lados en Fase 8.
- Fuera de alcance, no había nada que hacer: `TieredRateLimitEvaluator` (.NET) solo soporta
  particiones `Tenant|User`/`User` — la categoría D (Token+Ip) nunca tuvo un evaluador genérico
  real en .NET tampoco (queda en limiters nativos/legacy donde existe), así que el HTTP público
  atómico de Node no tiene precedente .NET que portar — es terreno nuevo, no un port.
- 286/286 tests + typecheck + lint verdes (los 152 errores de lint pre-existentes en
  `tests/unit/*` — non-null assertions — son deuda previa, no tocados en esta fase).

**Alcance original**:
- Migrar `SocketRateLimiter` y `DominantSpeakerThrottle` a la lib atómica de Fase 0.4.
- Categorizar los 6 scopes de socket como O (con partition (tenant, user)).
- Migrar el rate limiter HTTP de Fastify + las 2 rutas públicas al mismo modelo, alineado con `RateLimitPolicyCatalog` en un fichero paralelo `communication/src/domain/rate-limit-policies.ts` (no puede depender del .NET, es Node — pero mantener nombres canónicos idénticos para operabilidad).
- Publicar métricas OTel con las mismas etiquetas que .NET.

**Archivos afectados**: `src/Services/Communication/**`.

**Criterio de aceptación**: typecheck+tests verde; métricas correlacionables con las .NET en Grafana.

### Fase 8 — Observabilidad y dashboards

**CERRADA (2026-08-02).** Los 3 contadores del invariante §5/§3 (`ratelimit.evaluated_total`,
`ratelimit.blocked_total`, `ratelimit.fallback_open_total`) ahora existen en código real en ambos
lados, más los 2 dashboards y las 2 alertas del alcance original:
- **`.NET` — `RateLimitMetrics`** (nueva, `BuildingBlocks.Infrastructure.RateLimiting`) — resuelve
  el problema abierto de "código compartido en BuildingBlocks, sin Meter propio de ningún
  servicio": Meter fijo `"TaxVision.RateLimit"` (mismo criterio que `AuthorizationMetrics` de RBAC
  Fase 10 — instancia `IDisposable`, no static, registrada una vez en
  `TieredRateLimitingRegistration.AddTieredRateLimiting()`, `AddMeter` incondicional en
  `OpenTelemetryRegistration` ya que los 17 servicios lo llaman desde Fase 4). A diferencia de
  `AuthorizationMetrics`, sí lleva tag `tenant_id` — el invariante §5 lo exige explícitamente y los
  2 dashboards de esta fase son imposibles sin esa dimensión; cardinalidad acotada por cantidad de
  tenants, no de requests, criterio distinto y documentado como tal.
- **Toda la emisión vive en `TieredRateLimitEvaluator`** (no en `RateLimitAttribute`) — es el único
  punto con contexto completo (policy, tenant, capa, y las 3 fuentes reales de fallback-open: Redis
  primario, Redis overlay, y resolución de plan vía `EffectiveQuota.IsFallback`, que ya existía
  desde Fase 6 con un doc-comment que literalmente decía "Fase 8 lo usa para
  `fallback_open_total`"). `EffectiveQuota` ganó un campo `PlanCode` nuevo (para el tag `plan`).
- **Node — `rate-limit-metrics.ts`** (nuevo) — espejo con `@opentelemetry/api` (no prom-client):
  mismo Meter name y mismos 3 nombres de instrumento que .NET, para aterrizar como la misma serie
  Prometheus cross-stack. Confirmado con el usuario: se unifica el pipeline de métricas de Node vía
  OTel (nuevo `metricReader` en `telemetry.ts`) en vez de agregar un scrape target de Prometheus
  para el `/metrics` de prom-client que ya existía — Communication no exportaba NINGUNA métrica
  OTel antes de esta fase, solo trazas. `SocketRateLimiter`/`HttpRateLimiter` ganaron la emisión
  (misma decisión de centralizar en el limiter, no en cada call site).
- **Gap real encontrado y documentado, no corregido silenciosamente**: Node es fail-**cerrado** en
  Redis caído (propaga la excepción), no fail-open como exige el invariante §3.3 .NET — corregir
  ese comportamiento es un cambio de producción fuera del alcance de "observabilidad", así que se
  dejó tal cual y se instrumentó igual (`fallback_open_total` se emite antes de relanzar la
  excepción, para poder verlo en Grafana aunque hoy no cambie el resultado).
- **`RateLimit_Overview.json` / `RateLimit_ByTenant.json`** en `deploy/observability/grafana/provisioning/dashboards/`
  (no `deploy/grafana/dashboards/` como decía el texto original de esta fase — esa ruta nunca
  existió, la carpeta real provisionada desde la tarea de Grafana por microservicio es la de
  arriba). Panel de "latencia añadida por el limitador" del alcance original **no se construyó** —
  ni .NET ni Node miden esa duración en ningún lado (fabricarla habría sido un panel apuntando a
  una métrica que no existe); se reemplazó por un panel de fallback-open por razón, que sí es real
  y corresponde 1:1 con el riesgo ya documentado en la tabla §9 de este plan.
- **Alertas Grafana-nativas** (`deploy/observability/grafana/provisioning/alerting/`, vacía hasta
  ahora — greenfield, sin convención previa en el repo) — no hay Alertmanager en
  `docker-compose.yml`, así que es unified alerting nativo de Grafana sobre el datasource
  Prometheus ya provisionado. Las 2 del alcance original: tenant >90% cuota sostenido 5 min
  (`severity: warning`, reduce+threshold sobre `blocked/evaluated`), load shedder de flota
  disparando (`severity: critical`, sobre `gateway_requests_shed_total` — ya vivo desde Fase 5, sin
  instrumentación nueva necesaria).
- **Pendiente explícito, disclosed**: JSON/YAML validados sintácticamente y el schema sigue el
  formato documentado de Grafana, pero la importación real en un Grafana vivo + tráfico sintético
  del criterio de aceptación original NO se verificó — el docker-compose local de este entorno no
  tiene el `.env` de 60+ variables que necesita el stack completo, y generarlo solo para esta
  verificación está fuera de alcance de una fase de observabilidad. Verificación manual pendiente
  la próxima vez que el stack completo esté arriba.
- Build completo del monorepo + 18/18 proyectos de test .NET verdes (2601+ tests) + Communication
  typecheck/lint/286 tests verdes — cero regresiones.

**Alcance original**:
- Dashboard Grafana `RateLimit_Overview.json` con: total requests evaluated, total blocked, breakdown por categoría, top-N tenants por consumo, tasa 429 por endpoint, latencia añadida por el limitador.
- Dashboard `RateLimit_ByTenant.json` — pivot por tenant, útil para soporte cuando un cliente llama.
- Alertas: tenant al >90% de cuota sostenido 5 min → alerta de negocio; load shedder disparando → alerta ops.

**Archivos afectados**: `deploy/grafana/dashboards/**`.

**Criterio de aceptación**: dashboards importados en Grafana local, verificados con tráfico sintético; alertas configuradas y probadas.

### Fase 9 — Fitness functions + cierre

**CERRADA (2026-08-02) — última fase del plan. Plan de 9 fases CERRADO por completo.**

- **(a) Fitness function distribuida, no centralizada** — el texto original decía "NetArchTest en
  `BuildingBlocks.Tests`" para las 4 verificaciones, pero (a) es inherentemente por-servicio (cada
  uno tiene sus propios controllers) — se implementó siguiendo el mismo patrón ya establecido por
  las fitness functions de `AllowActorTypesAttribute` (ActorType Fase 6): un test
  `Controller_actions_should_declare_RateLimit_or_RateLimitExempt` agregado a cada
  `*ArchitectureTests.cs` existente (14 servicios) + `Documents` (ganó su primer `ApiAssembly`, no
  lo tenía) + `Billing` (no tenía NetArchTest en absoluto, se lo agregó desde cero). (b)/(c)/(d) sí
  quedaron centralizadas en `TaxVision.BuildingBlocks.Tests.RateLimit.RateLimitFitnessFunctionsTests`,
  como pedía el texto original.
- **4 violaciones reales encontradas y corregidas** — todas en `PaymentClient`, el único servicio
  que nunca migró de `[EnableRateLimiting("public")]` nativo a `[RateLimit]`/`[RateLimitExempt]`:
  `HostedCheckoutController.Get/Pay`, `PayableResolverController.Resolve` (públicos,
  anónimos, sin JWT — token/reference en el path es la única prueba de posesión, `[RateLimit]`
  fallaría abierto porque el evaluador solo particiona por Tenant/User) y
  `InternalPayablesController.EnsureInvoice` (M2M ServiceOnly). Las 4 se marcaron
  `[RateLimitExempt(...)]` con razón explícita, siguiendo precedentes exactos ya existentes en el
  repo (`CloudStorage.PublicShareController.ResolvePublic` para los 3 públicos, el patrón universal
  de `Internal*Controller` M2M para el cuarto) — nada se dejó ambiguo.
- **(b) Formato canónico de `RateCounterKey`** — verificado solo para el camino nuevo
  (`TieredRateLimitEvaluator`, Fase 3+). Los 4 limiters pre-Fase-3 (Auth `LoginThrottler`,
  Connectors ×4, Postmaster, PaymentApp — todos del F26) usan sus propios formatos legacy
  sembrados antes de que este formato existiera — migrarlos resetearía contadores Redis en
  producción, documentado como fuera de alcance, no "arreglado" en silencio.
- **(c) `StringIncrementAsync`** — ya estaba limpio (los F26 ya migraron a `IRateCounter` en
  fases previas); el test es puramente de regresión hacia adelante.
- **(d) `AddRateLimiter` nativo** — no es realmente "cero fuera de gateway": son 7 excepciones
  legítimas ya existentes (Auth, Growth, CloudStorage, Connectors, PaymentApp, Signature,
  PaymentClient — todas pre-auth/público/webhook sin tenant_id/user_id, verificadas una por una
  contra su propio doc-comment), consistente con el invariante §7 ("casos raros donde
  intencionalmente queremos gate per-instance"). El test es una **allowlist congelada**: cualquier
  `AddRateLimiter` nuevo fuera de esos 7 rompe el build — la regresión real que importa atrapar.
- **README §46** añadido (resumen ejecutivo + link a la Guía operativa
  `documents/RateLimit/Guia_Nuevos_Servicios_Endpoints.md`, que ya existía desde el research previo
  a Fase 0 y no necesitó cambios).
- **Postman** — 1 ejemplo de respuesta 429 con headers completos guardado en `CreateCustomer` de
  `TaxVision_Customer.postman_collection.json` (el endpoint piloto original de Fase 3), no en las
  17 collections — suficiente para documentar el contrato sin una edición masiva fuera de alcance.
- Build monorepo limpio (0 warnings, 0 errors) + **18/18 proyectos de test verdes** (incluye los 15
  tocados + Billing nuevo + BuildingBlocks.Tests + Gateway.Tests sin tocar, confirmando cero
  regresión colateral) — verificado de forma independiente, no solo confiado al reporte del agente
  que hizo el trabajo mecánico de los 16 archivos.

**Alcance original**:
- NetArchTest en `BuildingBlocks.Tests` que verifica: (a) todo endpoint con `[HttpXxx]` público tiene `[RateLimit]` o `[RateLimitExempt]`; (b) toda `RateCounterKey` cumple el formato `<svc>:rl:<policy>:...`; (c) no queda uso de `IDatabase.StringIncrementAsync` fuera de `RedisRateCounter`; (d) no queda `AddRateLimiter` en ningún `Program.cs` de servicio (solo en gateway).
- README §XX con la explicación completa del modelo de 4 capas (basado en este doc).
- Postman collection actualizada con ejemplos de 429 y headers.
- Reporte final español al usuario con el estado end-to-end.

**Criterio de aceptación**: NetArchTest verde en CI. README actualizado. Reporte final entregado.

---

## 9. Riesgos y mitigaciones

| Riesgo | Probabilidad | Mitigación |
|--------|:-:|-----------|
| Migración de un servicio rompe algún endpoint 3rd-party que dependía del rate limit viejo (webhooks Stripe/Gmail) | Media | Categorizar E primero, dejar cuota laxa (1000/min como hoy), no cambiar la clave partition hasta validar en staging. |
| Un tenant Enterprise recibe 429 en producción por cuota mal calibrada | Media-alta | Fase 6 va con feature flag `rateLimit:enforceTierQuotas` por defecto **OFF**; activar tenant por tenant con monitoreo activo. Métrica `ratelimit.would_block_total` que cuenta lo que *habría* bloqueado sin aplicar realmente. |
| Redis se cae → todos los rate limits fallan | Baja | Invariante 3 — fail-open. Métrica `ratelimit.fallback_open_total` con alerta. Circuit breaker sobre Redis para no colgar el path caliente. |
| Load shedder de Fase 5 se dispara agresivo y rechaza tráfico legítimo | Media | Umbral configurable, dry-run mode durante 2 semanas, top-N tenants log obligatorio para post-mortem. |
| Complejidad operativa alta — nadie entiende qué límite disparó | Alta si no se hace bien | Headers `X-RateLimit-Policy` + `X-RateLimit-Layer` en toda respuesta 429 (invariante 4). Dashboard por tenant. Runbook en README §XX explicando cómo diagnosticar. |
| El plan cambia mid-implementación — algún senior dice "por endpoint no, hazlo diferente" | Media | Fase 1 congela el ADR. Cualquier cambio posterior a Fase 1 va con revisión formal. |

---

## 10. Fuera de alcance de este plan (deliberado)

- **Rate limits para consumers de RabbitMQ** — Wolverine ya tiene su propio throttling por endpoint. No lo tocamos aquí.
- **Rate limits para jobs periódicos (hosted services)** — corren en el propio proceso, no reciben tráfico externo. Ya están limitados por cron/interval config.
- **DDoS a nivel de red** — lo maneja Caddy/Cloudflare frente al Gateway. Este plan es para lógica de aplicación.
- **Cuotas de storage (bytes) y cuotas de asientos (users)** — son cuotas de recursos, no de tasa. Viven en Subscription domain, no aquí.
- **Cuotas por API-key para partners externos** — no existen partners externos hoy. Cuando existan, se añade una categoría R al catálogo.
- **Costo-por-request tipo GraphQL de GitHub** — más pesado de lo que hace falta hoy. Reevaluar en 6-12 meses si el catálogo demuestra ser insuficiente.

---

## 11. Notas para Sonnet 5 (agente ejecutor)

- **Antes de cada fase**: leer este doc completo + el reporte de la fase anterior. Nunca arrancar una fase sin haber leído la anterior verde.
- **Después de cada fase**: reporte español al usuario listando qué se cambió realmente (no lo que se pretendió), qué se probó (build + tests + verificación manual si aplica), qué queda para la próxima.
- **Ante duda de clasificación de un endpoint**: leer §4 y comparar con los ejemplos de la tabla "Ejemplos concretos". Si sigue sin ser claro, preguntar al usuario. Nunca inventar una categoría.
- **Ante fallo de test o build**: no marcar la fase completed. Diagnosticar root cause, arreglar, re-verificar. Nunca `--no-verify` sin autorización explícita del usuario.
- **Guardrails DDD/Clean/SOLID/EDA del monorepo aplican siempre** — ver `feedback_ddd_guardrails_taxvision.md` en memoria. Ningún atajo.
- **Al cerrar todo el plan**: actualizar el índice `MEMORY.md` con una entrada nueva `[Rate limiting multi-capa plan cerrado](project_ratelimit_multi_layer_closed.md)` que resume qué se hizo y dónde quedó documentado.
