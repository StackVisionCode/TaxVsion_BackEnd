# Guía obligatoria — Rate limiting para nuevos endpoints, bounded contexts y microservicios

> **Cuándo se aplica esta guía**: siempre que agregues un endpoint HTTP público (`[HttpGet/Post/Put/Delete/Patch]`), un nuevo bounded context dentro de un servicio existente, o un microservicio nuevo entero. También cuando renombres o rediseñes un endpoint existente.
> **Documento hermano**: `Plan_Implementacion_Fases.md` — es el plan macro que Sonnet 5 ejecuta. Esta guía es lo que **tú, dev backend**, aplicas todos los días.
> **Regla dura**: ningún PR con endpoint público sin `[RateLimit]` o `[RateLimitExempt]` explícito pasa revisión. La fitness function del CI lo va a bloquear.

---

## 1. TL;DR — el modelo en 30 segundos

TaxVision usa **4 capas** de rate limiting, evaluadas en orden por cada request:

1. **Global infra** (Gateway) — última red de seguridad, se aplica a todo.
2. **Per-tenant** — partición primaria; escala con el plan tier del tenant.
3. **Per-user** — overlay dentro del tenant para que un script tóxico no apague al resto de su empresa.
4. **Per-endpoint** — cap propio para endpoints caros (búsqueda, bulk, rendering).

Tu trabajo cuando agregas un endpoint: **clasificarlo en una de las 17 categorías** del catálogo (§3), aplicar el atributo, y no romper los invariantes (§6). Todo lo demás lo hace el middleware.

---

## 2. Cuándo usarlo — decision tree

```
¿El endpoint es público (accesible externamente por HTTP o socket)?
├── NO (es un job interno, un consumer de Wolverine, un M2M interno de un job) → no aplica esta guía.
└── SÍ ↓

    ¿El endpoint requiere autenticación (JWT válido con tenant y user)?
    ├── NO → categorías A/B/C/D/E (pre-auth, público con token, webhook externo)
    │        Ver §3 bloque I.
    └── SÍ ↓

        ¿Qué HACE el endpoint?
        ├── Lee (GET) datos livianos (una fila, una lista corta) → F
        ├── Escribe (POST/PUT/PATCH/DELETE) una operación normal → G
        ├── Búsqueda / list con filtros / export / analytics → H
        ├── Bulk import, upload grande, ZIP download → I
        ├── Rendering / generación PDF / transcript / LLM → J
        ├── Envío externo (email, SMS, WhatsApp, push a proveedor) → K
        ├── Iniciar cobro / crear payment link / finalizar factura → L
        ├── Refund / cancelar suscripción / mover dinero saliente → M
        ├── Reveal SSN/ITIN/EIN/bank/PII sensible → N
        └── Realtime socket (chat, call, meeting) → O
```

Si no encaja en ninguna: **habla con arquitectura antes de crear una nueva categoría**. No inventes. El catálogo se extiende con revisión.

---

## 3. Categorías — versión resumida

Para la definición completa (algoritmo, cuota exacta, consecuencia al exceder, ejemplos), lee `Plan_Implementacion_Fases.md` §4. Aquí solo la lookup rápida.

| ID | Nombre | Partición primaria | Cuando aplicarla |
|----|--------|--------------------|------------------|
| A | Auth pre-tenant | email + IP | Login, refresh, MFA verify |
| B | Password/OTP flow | email + IP | Forgot password, reset, OTP resend, phone verify |
| C | Onboarding pre-tenant | email + IP | Checkout create, complete, subdomain-check |
| D | Público con token | IP + token/path | Share public, signature public, terms download, meeting join-by-token |
| E | Webhooks externos firmados | IP | Stripe, Gmail push, Graph notification, cualquier tercero que llame con firma HMAC |
| F | GET lectura ligera | (tenant, user) + tenant | Todo GET que devuelve <100 items sin agregación cara |
| G | Write ligero | (tenant, user) + tenant | POST/PUT/PATCH/DELETE típico, sin infra pesada |
| H | Búsqueda / listado pesado | (tenant, user) + tenant + endpoint | GET con `?search=`, `?filter=`, agregaciones, exports |
| I | Bulk / upload grande | (tenant, user) + tenant + endpoint | Imports, uploads multipart, ZIP downloads, cualquier cosa que consuma minutos |
| J | Rendering / cómputo caro | tenant + endpoint | PDF sealing, template rendering, transcript, LLM |
| K | Envío a proveedor externo | (tenant, account/provider) + global-per-provider | Envío email/SMS/push que consume cuota de tercero |
| L | Financiera — iniciar cobro | (tenant, user) + tenant + endpoint | Checkout create, payment link create |
| M | Financiera — admin | tenant + endpoint | Refund, cancel con reembolso, void — todo lo que mueve dinero saliente |
| N | Reveal sensible | user | GET que devuelve dato PII en claro |
| O | Realtime socket | (tenant, user) + scope | Chat send, call signal, meeting chat, dominant speaker |
| P | Health/observabilidad | ninguna | `/health/*`, `/metrics` |
| Q | Load shedder global | ninguna (Gateway) | No aplica al dev de servicio, corre en Gateway |

---

## 4. Cómo aplicarlo — .NET (los 17 servicios .NET)

### 4.1 Endpoint HTTP normal

```csharp
using BuildingBlocks.Web.RateLimiting;

[ApiController]
[Route("customers")]
[Authorize]
public sealed class CustomerController(IMessageBus bus) : ControllerBase
{
    [HttpPost]
    [HasPermission(CustomersPermissions.Manage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit(RateLimitPolicies.Customer.G_Create)]  // ← ESTO
    public async Task<IActionResult> Create(...) { ... }

    [HttpGet("{id:guid}/fiscal-profile/tax-identifier")]
    [HasPermission(CustomersPermissions.FiscalProfileReveal)]
    [RateLimit(RateLimitPolicies.Customer.N_FiscalReveal)]  // ← ESTO — categoría N (reveal)
    public async Task<IActionResult> RevealTaxIdentifier(...) { ... }
}
```

Las constantes viven en `src/BuildingBlocks/BuildingBlocks/RateLimiting/RateLimitPolicyCatalog.cs`. **Nunca** un string literal `"customer.g.create"` en un controller — siempre la constante. El nombre canónico es `<servicio>.<categoría>.<endpoint-slug>` en `snake_case` (ver Plan §6.1).

### 4.2 Endpoint que NO va rate-limited (health, metrics)

```csharp
[HttpGet("/health")]
[RateLimitExempt("health-check")]  // ← razón obligatoria como string
public IActionResult Health() => Ok();
```

Sin `RateLimitExempt`, el fitness test del CI **falla el build**.

### 4.3 Agregar una política nueva al catálogo

Editar `src/BuildingBlocks/BuildingBlocks/RateLimiting/RateLimitPolicyCatalog.cs`:

```csharp
public static class RateLimitPolicies
{
    public static class Customer
    {
        public const string G_Create        = "customer.g.create";
        public const string N_FiscalReveal  = "customer.n.fiscal_reveal";
        // ...
    }
}

// En el mismo archivo, el registry:
public static class RateLimitPolicyCatalog
{
    public static readonly IReadOnlyList<RateLimitPolicyDefinition> All = new[]
    {
        new RateLimitPolicyDefinition(
            Name: RateLimitPolicies.Customer.G_Create,
            Category: RateLimitCategory.G,
            PrimaryPartition: PartitionScope.TenantAndUser,
            OverlayLayers: [PartitionScope.Tenant],
            BaseQuotaPerMinute: 60,      // user-level base
            TenantBaseQuotaPerMinute: 600,
            WindowSeconds: 60,
            Algorithm: RateLimitAlgorithm.TokenBucket
        ),
        // ...
    };
}
```

Las cuotas listadas son **plan Standard**. El resolver aplica el multiplicador del tier del tenant en runtime.

### 4.4 Throttler de dominio (consumers, jobs)

Cuando el gate está dentro del handler, no en el endpoint HTTP (categoría K típicamente — envío a proveedor externo):

```csharp
using BuildingBlocks.Infrastructure.RateLimit;

public sealed class NotificationsEmailSendRequestedConsumer(
    IRateCounter rateCounter,   // ← inyectar
    IEmailSender sender)
{
    public async Task Handle(EmailSendRequested msg, CancellationToken ct)
    {
        var key = RateCounterKey.Build(
            policy: RateLimitPolicies.Postmaster.K_Dispatch,
            parts: ["tenant", msg.TenantId.ToString("N"), "provider", msg.ProviderCode]);

        var effective = await quotaResolver.ResolveAsync(RateLimitPolicies.Postmaster.K_Dispatch, msg.TenantId, ct);

        var count = await rateCounter.IncrementAndGetAsync(key, TimeSpan.FromSeconds(effective.WindowSeconds), ct);

        if (count > effective.PermitCount)
        {
            // Consecuencia según categoría — aquí K = marcar como RateLimited terminal
            sentMessage.MarkAsFailed($"RateLimited: retry after {effective.WindowSeconds}s");
            return;
        }

        await sender.SendAsync(...);
    }
}
```

Reglas de oro:
- **Nunca** `IDatabase.StringIncrementAsync` directo. Solo `IRateCounter`. NetArchTest lo bloquea.
- **Nunca** `ICacheService.GetAsync` + `SetAsync` para contadores. Es el TOCTOU bug que F26 cerró.
- **Nunca** construir keys con `$"..."`. Siempre `RateCounterKey.Build(...)`.

---

## 5. Cómo aplicarlo — Node (Communication, TranscriptWorker)

### 5.1 Endpoint HTTP (Fastify)

```typescript
import { rateLimitPolicy } from '@/domain/rate-limit-policies';
import { rateLimitPreHandler } from '@/infrastructure/http/rate-limit-pre-handler';

fastify.post('/communication/meetings/join-by-token', {
  preHandler: rateLimitPreHandler(rateLimitPolicy('communication.d.meeting_join_by_token')),
  schema: { ... }
}, async (req, reply) => { ... });
```

Las políticas viven en `src/Services/Communication/src/domain/rate-limit-policies.ts`. Los nombres canónicos son **idénticos** a los del catálogo .NET (mismo naming convention `<svc>.<cat>.<slug>`), para que Grafana correlacione.

### 5.2 Socket handler

```typescript
import { socketRateLimit } from '@/infrastructure/redis/socket-rate-limiter';

socket.on('chat.send', async (data, ack) => {
  const check = await socketRateLimit({
    policy: 'communication.o.chat_send',
    tenantId: ctx.tenantId,
    userId: ctx.userId,
  });

  if (!check.allowed) {
    return ack({ ok: false, code: 'chat.RateLimited', retryAfterMs: check.retryAfterMs });
  }

  // ... resto del handler
});
```

`socket-rate-limiter.ts` internamente usa el helper atómico `rate-counter.ts` (Lua EVAL) — **no** `INCR + EXPIRE` separados (bug arreglado en Fase 0.4 del plan).

---

## 6. Invariantes (los que el fitness test verifica)

Si tu PR viola alguno de estos, CI falla. Léelos antes de escribir código.

1. **Todo endpoint público tiene `[RateLimit]` o `[RateLimitExempt]`**. No hay tercera opción.
2. **La partición primaria coincide con la definida por la categoría** (§3). No inventes.
3. **Fail-open si Redis se cae**. Si tu handler custom lee `IRateCounter` y hay excepción, permitir el request + emitir métrica `ratelimit.fallback_open_total{policy}`. Nunca fallar el request por un fallo del limitador.
4. **429 lleva headers estándar**: `Retry-After`, `X-RateLimit-Policy`, `X-RateLimit-Layer`, `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset`. El middleware los añade automáticamente si usas `[RateLimit]` — si haces gate manual (throttler dominio), tú los añades.
5. **Cuota por categoría, no por endpoint puntual**. Si tu endpoint necesita una cuota "muy diferente" de la categoría a la que naturalmente pertenece, probablemente esté mal clasificado. Revisá el árbol de decisión.
6. **Pre-auth y webhooks nunca por tenant**. Categorías A/B/C/E — la partición es email/IP porque el tenant es lo que se está creando o el origen es un tercero.
7. **Nunca desactivar el rate limit "temporalmente en producción" con un flag ad-hoc**. Si un cliente Enterprise reporta 429 falsos, se abre ticket, se revisa su tier, se ajusta la fila en `PlanRateLimits` (Fase 6). Suba puntual sin trazabilidad → prohibido.
8. **Health y metrics siempre exentos**. Categoría P. Sin excepción.
9. **`RateCounterKey` sigue el formato `<svc>:rl:<policy>:<parts>:<bucket>`**. Ningún key ad-hoc.
10. **Tests**: cada endpoint nuevo debe tener al menos 1 test de integración que verifique 429 al exceder cuota (mock del clock o del contador para no depender de tiempo real).

---

## 7. Anti-patrones — no hagas esto

| Anti-patrón | Por qué está mal | Qué hacer en su lugar |
|-------------|------------------|-----------------------|
| `[EnableRateLimiting("mi-nueva-policy")]` con `AddFixedWindowLimiter` en `Program.cs` | Es in-memory por réplica — con N réplicas el límite efectivo es N×. Además no es tier-aware. | Usar `[RateLimit(RateLimitPolicies.Customer.G_Create)]` con el catálogo. El middleware unificado usa Redis. |
| Rate limitar por IP en un endpoint autenticado | Un tenant corporativo detrás de proxy = 500 users compartiendo 1 IP. Un usuario en 4G = IP nueva cada 5 min. IP raw no sirve en autenticado. | Usar (tenant, user) para autenticados. IP solo para pre-auth (A/B/C) y webhooks (E). |
| Un solo rate limit "global" para todo el servicio | Un endpoint pesado (búsqueda) consume la cuota que necesitaba un endpoint liviano (GET id). | Categorizar cada endpoint individualmente. F ≠ H ≠ I. |
| Cuota hard-coded en el controller (`[EnableRateLimiting(...)]` con `PermitLimit: 100`) | No respeta el plan tier del tenant. Un Enterprise que paga 10× el precio recibe la misma cuota que un Free. | Cuota en el catálogo, multiplicador por plan en Fase 6. Endpoint solo declara la política. |
| `try { await limiter.CheckAsync(...); } catch { return 429; }` | Fail-CLOSED al fallo de Redis = un fallo de infra apaga todo el servicio. | Fail-OPEN al fallo de Redis + métrica. Invariante 3. |
| Reutilizar una política de otro servicio en el propio (`RateLimitPolicies.Auth.A_Login` en Customer) | Contamina el catálogo, rompe telemetría, contradice el naming canónico. | Cada servicio declara sus propias políticas dentro de su clase estática (`RateLimitPolicies.Customer.*`). |
| Rate limitar consumers de Wolverine con `IRateCounter` "porque sí" | Wolverine ya tiene throttling propio por endpoint. Aplicar `IRateCounter` a un consumer solo tiene sentido cuando la razón es proteger un recurso externo (categoría K) o cuotas comerciales visibles al tenant. | Consumers internos sin externalidad → dejar sin rate limit propio. Solo K aplica. |
| Contar en SQL con `UPDATE ... SET count = count + 1` para rate limit | SQL no es cache. Explota el DB en burst. | Redis + `IRateCounter`. SQL solo para lockouts persistentes (User.FailedLoginCount, PaymentLink.FailedRedemptionAttempts). |
| Devolver 429 sin `Retry-After` | El cliente/frontend/mobile no sabe cuándo reintentar → tormenta de reintentos. | Siempre `Retry-After` en segundos, calculado desde el TTL real de la clave. |
| Silenciar el 429 (log warning + return 200 vacío) para "no romper al cliente" | El cliente cree que funcionó, el rate limit no funciona, el sistema se cae eventualmente. | 429 explícito con contrato de headers. El cliente decide UX. |

---

## 8. Checklist antes del PR

Copiá esto en la descripción del PR. Si algo no aplica, marcalo `N/A` con la razón.

```
### Rate limiting
- [ ] Cada endpoint público nuevo tiene [RateLimit(...)] o [RateLimitExempt("razón")]
- [ ] Toda política nueva está en RateLimitPolicyCatalog con categoría (A..O) explícita
- [ ] La partición coincide con la categoría (ver Guía §3)
- [ ] Si es categoría K (envío externo): overlay per-provider global cap está aplicado
- [ ] Si es categoría M (admin financiero): AuthAuditLog está emitido incluso al 429
- [ ] Si es categoría N (reveal sensible): partition es user (no tenant), AuthAuditLog obligatorio
- [ ] No hay `IDatabase.StringIncrementAsync` directo fuera de RedisRateCounter
- [ ] No hay `ICacheService.GetAsync + SetAsync` para contadores
- [ ] No hay keys construidas con string interpolation — solo RateCounterKey.Build(...)
- [ ] Test de integración que verifica 429 con headers correctos
- [ ] Métrica OTel `ratelimit.evaluated_total{policy}` emitida
- [ ] README/Postman actualizados con la política nueva y su respuesta 429 de ejemplo
```

---

## 9. Cómo probar rate limiting localmente

### 9.1 Test unitario del handler (mock del contador)

```csharp
public class CreateCustomerHandlerTests
{
    [Fact]
    public async Task Rejects_when_tenant_over_quota()
    {
        var counter = Substitute.For<IRateCounter>();
        counter.IncrementAndGetAsync(Arg.Any<RateCounterKey>(), Arg.Any<TimeSpan>(), default)
               .Returns(Task.FromResult(601L));  // tenant base 600/min → excede

        var handler = new CreateCustomerHandler(..., counter);

        var result = await handler.Handle(new CreateCustomerCommand(...), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RateLimit.Exceeded");
    }
}
```

### 9.2 Test de integración (WebApplicationFactory + Redis real de test)

Usar la fixture `RedisTestcontainerFixture` del proyecto (agregada en Fase 3). Ejemplo:

```csharp
public class CreateCustomerRateLimitTests(RedisTestcontainerFixture redis)
    : IClassFixture<RedisTestcontainerFixture>
{
    [Fact]
    public async Task Returns_429_with_correct_headers_after_quota_exceeded()
    {
        var client = _factory.CreateClient();
        await AuthenticateAsTenantAdmin(client);

        // Standard: 60/min per user. Enviar 61.
        for (int i = 0; i < 60; i++)
            await client.PostAsJsonAsync("/customers", ValidBody());

        var response = await client.PostAsJsonAsync("/customers", ValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.Should().Contain(h => h.Key == "Retry-After");
        response.Headers.Should().Contain(h => h.Key == "X-RateLimit-Policy" && h.Value.First() == "customer.g.create");
        response.Headers.Should().Contain(h => h.Key == "X-RateLimit-Layer" && h.Value.First() == "user");
    }
}
```

### 9.3 Verificación manual con curl

```bash
TOKEN=$(cat token.txt)
for i in {1..70}; do
  echo "Request $i:"
  curl -s -o /dev/null -w "HTTP %{http_code} — X-RateLimit-Remaining: %header{X-RateLimit-Remaining}\n" \
    -X POST http://localhost:5047/customers \
    -H "Authorization: Bearer $TOKEN" \
    -H "Content-Type: application/json" \
    -d '{"kind":"Individual","firstName":"Test","primaryEmail":"t@example.com","language":"Es","preferredChannel":"Email"}'
done
```

Se espera ver un 429 alrededor del request 61 (Standard: 60/min per user), con `Retry-After` en el header.

---

## 10. Cuando tengas dudas

En este orden:

1. **Releer §3 (categorías) y §4 del Plan de Implementación**. La mayoría de dudas se contestan ahí.
2. **Buscar un endpoint parecido ya migrado en el mismo servicio** (`grep -r "[RateLimit(" src/Services/...`). Copiar patrón.
3. **Preguntar en el canal de arquitectura**. No inventar categoría, no bypassear el middleware, no "hacerlo así por ahora y después lo arreglamos".

Este es el modelo que usan Stripe, GitHub, Shopify y Atlassian. El proyecto ya lo tiene decidido. Tu trabajo es aplicarlo bien, no rediseñarlo.
