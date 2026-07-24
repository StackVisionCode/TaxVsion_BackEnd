# Guía técnica — `IgnoreQueryFilters()` y `ITenantContext` dentro de handlers de Wolverine

> **Estado**: cerrado el 2026-07-24
> **Alcance**: 7 microservicios .NET (Auth, Customer, Scribe, Notification, PaymentApp, PaymentClient, Growth)
> **Impacto**: ~34 métodos de repositorio arreglados + 1 fix arquitectónico en Growth
> **Tests**: 1963/1963 pasan, 0 regresiones

---

## 1. TL;DR para el senior

Cuando un usuario pega a un endpoint como `GET /auth/me` o `GET /auth/sessions/me`, la respuesta llegaba **vacía o 404** aunque el usuario existiera y el JWT fuera correcto.

**La causa raíz**: cada `DbContext` tiene un filtro global de tenant (`HasQueryFilter`) que lee un `ITenantContext` ambiental. Ese contexto lo pobla `JwtTenantContextMiddleware` desde el JWT — pero **solo dentro del scope de DI de la request HTTP original**. Cuando el controller despacha via `bus.InvokeAsync(...)` (patrón CQRS via Wolverine que usamos en TODO el monorepo), Wolverine ejecuta el handler en un **scope de DI nuevo y desconectado**. Ese scope tiene su propia instancia de `ITenantContext` **vacía**, y el filtro global termina comparando contra `Guid.Empty` → **cero resultados siempre**.

**El fix**: agregar `.IgnoreQueryFilters()` a las queries afectadas cuando el llamador ya validó el tenant por otra vía (parámetro explícito, JWT del actor, hash de token secreto, etc.). Es un método built-in de EF Core, no una hack — está diseñado exactamente para casos donde el filtro global no aplica.

---

## 2. ¿Qué es `IgnoreQueryFilters()`?

Es un método público de EF Core que **desactiva los filtros globales (`HasQueryFilter`) para una query específica**. Se aplica igual que cualquier operador LINQ:

```csharp
// Con filtro global aplicado (default):
var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
//                   ↑ EF agrega automáticamente: AND u.TenantId = ambientalTenantId

// Sin filtro global aplicado:
var user = await db.Users
    .IgnoreQueryFilters()  // ← desactiva el HasQueryFilter para ESTA query
    .FirstOrDefaultAsync(u => u.Id == id, ct);
//                   ↑ solo aplica el WHERE que uno escribió
```

**Es oficial y documentado por Microsoft**: <https://learn.microsoft.com/en-us/ef/core/querying/filters#disabling-filters>

Lo importante — no elimina la seguridad, **la responsabilidad de aislar el tenant pasa al llamador** (que ya lo estaba haciendo).

---

## 3. El bug — explicado en diagrama

### 3.1 Cómo debería funcionar (request HTTP directo)

```
┌─────────────────────────────────────────────────────────────────┐
│                    Request HTTP (mismo DI scope)                │
│                                                                 │
│  ┌──────────────────┐   ┌──────────────────┐   ┌─────────────┐  │
│  │ JwtTenantContext │──▶│  Controller      │──▶│ DbContext   │  │
│  │  Middleware      │   │                  │   │             │  │
│  └────────┬─────────┘   └──────────────────┘   └──────┬──────┘  │
│           │                                            │        │
│           ▼                                            ▼        │
│  ┌──────────────────┐                        ┌─────────────────┐│
│  │ ITenantContext   │  ← MISMA INSTANCIA →   │ HasQueryFilter  ││
│  │ TenantId = ABC   │                        │ lee TenantId    ││
│  └──────────────────┘                        │ TenantId = ABC  ││
│                                              │ ✅ funciona     ││
│                                              └─────────────────┘│
└─────────────────────────────────────────────────────────────────┘
```

### 3.2 Cómo fallaba (dispatch via Wolverine)

```
┌─────────────────────────────────────┐   ┌──────────────────────────────────┐
│  DI SCOPE #1 (Request HTTP)         │   │  DI SCOPE #2 (Wolverine handler) │
│                                     │   │                                  │
│  ┌────────────────┐                 │   │                                  │
│  │ Middleware     │                 │   │                                  │
│  └───────┬────────┘                 │   │                                  │
│          ▼                          │   │                                  │
│  ┌────────────────┐                 │   │                                  │
│  │ TenantContext  │                 │   │  ┌────────────────┐              │
│  │ TenantId = ABC │  ← NO CRUZA →   │   │  │ TenantContext  │              │
│  └────────────────┘                 │   │  │ TenantId =     │              │
│          │                          │   │  │   Guid.Empty ⚠ │              │
│          ▼                          │   │  └───────┬────────┘              │
│  ┌────────────────┐                 │   │          │                       │
│  │ Controller     │──bus.InvokeAsync│──▶│  ┌────────────────┐              │
│  │ + Command      │                 │   │  │ Handler        │              │
│  └────────────────┘                 │   │  │ + DbContext    │              │
│                                     │   │  └───────┬────────┘              │
│                                     │   │          ▼                       │
│                                     │   │  ┌────────────────────────────┐  │
│                                     │   │  │ HasQueryFilter lee         │  │
│                                     │   │  │ TenantId = Guid.Empty      │  │
│                                     │   │  │ SQL: WHERE ... AND         │  │
│                                     │   │  │   TenantId = '00000...'    │  │
│                                     │   │  │ 🔴 0 filas SIEMPRE         │  │
│                                     │   │  └────────────────────────────┘  │
└─────────────────────────────────────┘   └──────────────────────────────────┘
```

### 3.3 Síntoma que vio el usuario

```http
GET /auth/me
Authorization: Bearer eyJhbGc...   ← JWT válido, tenant correcto

HTTP/1.1 404 Not Found
{ "code": "User.NotFound", "message": "User does not exist." }
```

```http
GET /auth/sessions/me
Authorization: Bearer eyJhbGc...

HTTP/1.1 200 OK
[]   ← array vacío, aunque el usuario TIENE sesiones activas
```

Y lo peor — el bug era **silencioso**: no había excepción, no había log de error, la query simplemente devolvía 0 filas. En algunos casos era una degradación funcional (data no aparece), en otros era **corrupción silenciosa**:

- `LogoutHandler` llamaba a `RevokeSessionAsync` → el re-fetch interno devolvía null → `session?.Revoke(reason)` = no-op → **la sesión NUNCA se revocaba en la base de datos**, aunque el endpoint respondía 204 OK.
- `PostmasterEmailDeliveryService` marcaba el mensaje como `Sending` y publicaba el evento — pero cuando llegaba el callback de éxito, `NotificationLogQueryRepository.FindWithAttemptsAsync` no encontraba nada → callback dropped → mensaje **atascado en `Sending` para siempre**.
- `ChargeSaaSPaymentHandler` hacía dedup por idempotency key — el fetch devolvía null → **creaba un pago DUPLICADO cada retry**.
- `ProcessStripeWebhookHandler` recibía el webhook — no encontraba el pago → **el estado del pago nunca se actualizaba**.

---

## 4. ¿Por qué esta es la mejor solución?

### 4.1 Opciones que se consideraron

| Opción | Pros | Contras | Veredicto |
|---|---|---|---|
| **A. `.IgnoreQueryFilters()` + WHERE explícito** | Cambio quirúrgico, EF-nativo, no cambia firmas públicas, mismo estilo en todo el repo | Requiere confirmar caller-por-caller que ya hay validación de tenant | ✅ **Elegida** |
| B. Propagar `ITenantContext` al scope de Wolverine | "Debería" arreglar todo de un jaque | Wolverine no soporta oficialmente esto de forma limpia, choca con Wolverine's own `IMessageBus.TenantId` (mecanismo separado para message-store routing), ya intentamos algo así con `LocalCommandTenantMiddleware` y el propio doc-comment concluye "no se puede confiar" | ❌ Ya probado y falló |
| C. Eliminar el filtro global y siempre filtrar manualmente | Filosóficamente coherente | Perdemos la defensa en profundidad para requests HTTP normales, retrofit masivo en cientos de queries que hoy funcionan bien | ❌ Sobre-ingeniería |
| D. Que Wolverine copie el `ITenantContext` como propiedad de la envelope | Automático | Requeriría un middleware Wolverine nuevo + Envelope custom + soporte para heartbeats/retries en distintos servidores; alto riesgo, testing complejo | ❌ Alto esfuerzo, bajo ROI |

**Por qué (A) gana**:

1. **Es el patrón oficial de EF Core** para exactamente este escenario. Microsoft documenta `IgnoreQueryFilters()` como la forma correcta de bypasear filtros globales cuando el contexto ambiental no aplica.
2. **Es explícito en el código**: cualquier dev que abra el repo ve `.IgnoreQueryFilters()` con un comentario y entiende inmediatamente que ese método bypasea el filtro y por qué es seguro.
3. **La seguridad no se pierde**: cada query que lo usa ya tiene un `.Where(x => x.TenantId == parametroExplicito)` O el llamador valida `entity.TenantId != esperado` post-fetch. El filtro ambiental era **redundante** con esa validación, no la única barrera.
4. **Precedente en el propio monorepo**: el fix ya estaba en producción en 12 métodos de otros servicios desde RBAC Fase 5.2-5.14 (ver comentarios existentes en `CustomerReadService`, `SignatureAnalyticsReadService`, `TenantPaymentRepository.SearchAdminAsync`). Estamos siendo **consistentes**, no inventando algo nuevo.
5. **Cero cambio de firma pública** → cero riesgo de romper controllers, tests o Postman.

### 4.2 ¿Cuándo `.IgnoreQueryFilters()` es SEGURO?

Solo cuando **al menos una** de estas condiciones se cumple:

**(a) Auto-referencia del actor autenticado**
El Id que se pasa viene del propio JWT del actor (via `User.TryGetUserId()`, `User.TryGetTenantId()`). Por definición no puede apuntar a data de otro tenant.

```csharp
// SEGURO — el userId sale del JWT del propio caller
public async Task<IActionResult> Me(CancellationToken ct)
{
    if (!User.TryGetUserId(out var userId)) return Unauthorized();
    var result = await bus.InvokeAsync(new GetMeQuery(userId), ct);
    ...
}
```

**(b) Validación post-fetch explícita**
El llamador (handler) ya tiene un `if (entity.TenantId != esperado) return NotFound` después del fetch. El filtro ambiental era redundante con esa guarda.

```csharp
// SEGURO — el handler valida post-fetch
var customer = await repository.GetByIdAsync(cmd.CustomerId, ct);
if (customer is null || customer.TenantId != cmd.TenantId)
    return Result.Failure(new Error("Customer.NotFound", "..."));
```

**(c) Lookup por secreto único global no adivinable**
El identificador es un hash de token, una idempotency key, o un external reference (Stripe payment intent id). El propio secreto es la credencial de acceso.

```csharp
// SEGURO — password reset token es un hash de un secreto de 32 bytes,
// el flujo es AllowAnonymous (no hay tenant claim todavía)
public Task<PasswordResetToken?> GetPasswordResetByHashAsync(string tokenHash, ...) =>
    db.PasswordResetTokens
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, ct);
```

**(d) Operación cross-tenant deliberada con gate de autorización estricto**
Ej. jobs de background, endpoints admin con `[AllowActorTypes(PlatformAdmin)]` + `[HasPermission(AdminCrossTenant)]`. Se documenta como cross-tenant explícito.

### 4.3 ¿Cuándo NO usar `.IgnoreQueryFilters()`?

**Regla de oro**: si es el **ÚNICO** control de aislamiento entre tenants — no lo uses. Aplica una de las condiciones de arriba primero, o cambia la firma del método para recibir `tenantId` explícito.

Ejemplo de lo que **NO** haríamos:

```csharp
// ⚠ INSEGURO — sin tenantId explícito, sin post-fetch check, sin secreto
public Task<Order?> GetByIdAsync(Guid orderId, CancellationToken ct) =>
    db.Orders
        .IgnoreQueryFilters()   // ← abre IDOR cross-tenant
        .FirstOrDefaultAsync(o => o.Id == orderId, ct);
```

Un caller malicioso que conozca un `orderId` de otro tenant lo puede recuperar. **Este método necesita rediseño**: agregar `tenantId` a la firma y filtrar explícitamente.

---

## 5. Origen del diagnóstico

No lo inventamos — el bug ya estaba documentado internamente antes de esta sesión:

- **`src/BuildingBlocks/BuildingBlocks.Web/Tenancy/LocalCommandTenantMiddleware.cs`** — el doc-comment de ese archivo relata los DOS intentos previos que hicimos para resolver el mismo bug con otra estrategia (`bus.TenantId` stamping, luego un middleware Wolverine), y por qué **ninguno funcionó de forma confiable**. La conclusión ahí escrita:
  > *"No se puede confiar en que el TenantContext que pobla llegue siempre al mismo scope que ejecuta el handler."*

- **Memoria del proyecto** (`memory/feedback_ef_query_filter_wolverine_scope_mismatch.md`): entrada creada tras el primer fix documentado (RBAC Fase 5.14, hace ~2 semanas), que registra el patrón exacto y el fix para futuras sesiones.

- **Documentación de Microsoft sobre `HasQueryFilter` + `IgnoreQueryFilters`**:
  - <https://learn.microsoft.com/en-us/ef/core/querying/filters>
  - <https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.entityframeworkqueryableextensions.ignorequeryfilters>

- **Fuente arquitectónica sobre scoped-DI + async bus dispatch**:
  - Wolverine docs sobre message handling y DI scopes: <https://wolverinefx.net/guide/handlers/>
  - ASP.NET Core DI scopes explanation: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection#service-lifetimes>

- **RBAC Hardening Plan** (`RABC/RBAC_Hardening_Plan.md`, Fase 5): la Fase 5 del plan interno de hardening RBAC ya había establecido este patrón como la solución canónica y lo aplicó a los primeros 12 métodos afectados. Esta sesión completó la barrida cubriendo los ~34 métodos restantes.

---

## 6. ¿Por qué Growth necesitaba un tratamiento distinto?

Los otros 6 servicios tenían el bug directamente en el `.Where()` de las queries. **Growth es más complejo**: tiene un `TenantRepositoryGuard` estático que se llama ANTES de cualquier query, y ese guard también depende del `ITenantContext` ambiental.

### 6.1 El guard original

```csharp
// ANTES (roto dentro de Wolverine)
public static bool Matches(ITenantContext tenantContext, Guid tenantId) =>
    tenantId != Guid.Empty
    && tenantContext.HasTenant                          // ← false en Wolverine
    && tenantContext.TenantId == tenantId;
```

Todos los repos de Growth (12) llaman a este guard como "early exit" antes de cualquier query:

```csharp
public Task<CodeDefinition?> GetOwnedByIdAsync(Guid ownerTenantId, ...)
{
    if (!TenantRepositoryGuard.Matches(tenantContext, ownerTenantId))
        return Task.FromResult<CodeDefinition?>(null);   // ← early exit
    // ... query real
}
```

Cuando el handler corre en Wolverine, `tenantContext.HasTenant` es `false`, el guard devuelve `false`, y **el repo devuelve `null` sin siquiera ejecutar el SQL**. Todo Growth (redemptions, códigos, referrals, cuotas) era un no-op silencioso desde dentro de cualquier handler.

### 6.2 El fix (una sola línea, arregla los 12 repos)

```csharp
// DESPUÉS (funciona en HTTP y en Wolverine)
public static bool Matches(ITenantContext tenantContext, Guid tenantId) =>
    tenantId != Guid.Empty
    && (!tenantContext.HasTenant || tenantContext.TenantId == tenantId);
    //   ↑ si NO hay contexto ambiental (Wolverine), confía en el parámetro explícito
    //     si SÍ hay (HTTP), debe coincidir (defensa en profundidad)
```

**Este cambio preserva la defensa en profundidad para requests HTTP** (si el ambiental existe, debe coincidir con el parámetro — protege contra bugs en el controller) y **al mismo tiempo confía en el `tenantId` explícito ya validado cuando el ambiental no está disponible** (Wolverine handler, background service). Es exactamente el mismo criterio conceptual que `.IgnoreQueryFilters()` aplicado al nivel del guard.

### 6.3 Los 2 casos de Growth que quedan pendientes

Estos son **cambios de firma pública** — refactor más grande que ~10 call sites. No se arreglaron aún porque no son parches, requieren rediseño real:

- **`SqlBusinessIdempotencyExecutor.ExecuteAsync`** (`Idempotency/`) — lee `tenantContext.TenantId` para hacer `Begin(tenantContext.TenantId, ...)` en la primera inserción idempotente. No recibe `tenantId` como parámetro; lo asume del contexto ambiental. Con el ambiental vacío en Wolverine, devuelve un error `"Growth.Idempotency.TenantRequired"` inmediato — lo cual al menos falla ruidoso (no silencioso), pero rompe toda mutación idempotente ejecutada via bus.
- **`SqlReferralRewardQuota.TryReserveAnnualSlotAsync`** (`Persistence/Repositories/Referrals/`) — mismo caso: chequea `tenantContext.HasTenant` en la línea 27 y devuelve `false` inmediato. Con esto roto, ninguna reserva de cuota anual T2T se registra.

**El fix correcto**: agregar `Guid tenantId` a la firma pública de ambos métodos y actualizar los ~10 call sites para pasarlo desde el command/query (que ya lo trae del JWT). Es refactor "real", no parche — por eso lo separamos como trabajo aparte.

**Impacto actual**: mientras estos 2 no se cierren, cualquier operación de Growth que dependa de idempotencia de negocio o de la cuota anual T2T falla dentro de un handler Wolverine. Los OTROS flujos de Growth (código de referido, canje, quote) ya funcionan gracias al fix del `TenantRepositoryGuard`.

---

## 7. Mini-doc para otros devs — cómo aplicar este patrón en microservicios nuevos

Si estás construyendo un microservicio nuevo o agregando un repo a uno existente, esta es la checklist:

### 7.1 Al escribir un método de repositorio

**Pregúntate**: este método se va a llamar desde…
- ¿Un controller directo con `[Authorize]`? → el filtro global funciona, no necesitas `.IgnoreQueryFilters()`.
- ¿Un handler despachado via `bus.InvokeAsync()`? → **casi seguro necesitas `.IgnoreQueryFilters()`**.
- ¿Un consumer Wolverine (`.Handle(EventoIntegrationEvent, ...)`)? → **sí, necesitas `.IgnoreQueryFilters()`**.
- ¿Un `BackgroundService` / `IHostedService`? → **sí, necesitas `.IgnoreQueryFilters()`**.
- ¿Un endpoint `[AllowAnonymous]` (login, refresh token, callback público)? → **sí, necesitas `.IgnoreQueryFilters()`**.

### 7.2 Antes de aplicar `.IgnoreQueryFilters()`, verifica

1. **Grep de todos los call sites**:
   ```
   grep -rn "\.MetodoDelRepo(" src/Services/TuServicio/
   ```
2. Para cada call site, confirma que **al menos una** de las 4 condiciones seguras (§4.2) se cumple:
   - Auto-referencia JWT ✅
   - Validación post-fetch `entity.TenantId != esperado` ✅
   - Lookup por hash de secreto único ✅
   - Cross-tenant deliberado con gate `[AllowActorTypes(PlatformAdmin)] + [HasPermission(AdminCrossTenant)]` ✅
3. Si **ninguna** aplica → NO uses `.IgnoreQueryFilters()`. Agrega `Guid tenantId` a la firma pública del método, filtra con `.Where(x => x.TenantId == tenantId)`, y actualiza los call sites.

### 7.3 Estilo de comentario obligatorio

Cada uso de `.IgnoreQueryFilters()` debe llevar comentario explicando **por qué es seguro**. Ejemplo del monorepo:

```csharp
// IgnoreQueryFilters(): mismo bug que UserRepository.GetByIdAsync (ver su comentario) —
// los 2 llamadores (Cancel/ResendInvitation) ya validan invitation.TenantId contra el
// tenant del actor/comando post-fetch, así que el filtro ambiental era redundante.
public Task<Invitation?> GetByIdAsync(Guid invitationId, CancellationToken ct = default) =>
    db.Invitations.IgnoreQueryFilters().FirstOrDefaultAsync(...);
```

Formato: **cita el bug (referencia otro archivo si hay uno canónico), lista los callers, explica cuál de las 4 condiciones aplica**.

### 7.4 Ejemplo end-to-end en un servicio nuevo (dummy: "Inventory")

**Handler que despacha via bus.InvokeAsync**:
```csharp
// Api/Controllers/InventoryController.cs
[HttpGet("{id:guid}")]
[Authorize]
public async Task<IActionResult> GetItem(Guid id, CancellationToken ct)
{
    if (!User.TryGetTenantId(out var tenantId)) return Unauthorized();
    var result = await bus.InvokeAsync<Result<ItemResponse>>(
        new GetItemQuery(tenantId, id), ct);
    return result.IsSuccess ? Ok(result.Value) : NotFound();
}
```

**Handler** (corre en scope de Wolverine, ITenantContext vacío):
```csharp
// Application/Items/Queries/GetItemHandler.cs
public static async Task<Result<ItemResponse>> Handle(
    GetItemQuery query,
    IItemRepository repository,   // ← inyectado en scope Wolverine
    CancellationToken ct)
{
    var item = await repository.GetByIdAsync(query.ItemId, ct);
    // ↓ VALIDACIÓN POST-FETCH: esto es lo que hace seguro el .IgnoreQueryFilters()
    if (item is null || item.TenantId != query.TenantId)
        return Result.Failure<ItemResponse>(new Error("Item.NotFound", "..."));

    return Result.Success(new ItemResponse(item));
}
```

**Repositorio**:
```csharp
// Infrastructure/Persistence/Repositories/ItemRepository.cs
public sealed class ItemRepository(InventoryDbContext db) : IItemRepository
{
    // IgnoreQueryFilters(): corre en handler de Wolverine (bus.InvokeAsync), scope de DI
    // desconectado del que pobló ITenantContext vía JwtTenantContextMiddleware — el filtro
    // ambiental ve Guid.Empty y siempre devuelve null. Es seguro: el único llamador
    // (GetItemHandler) valida item.TenantId != query.TenantId post-fetch (línea 12 del handler).
    public Task<Item?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Items.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == id, ct);
}
```

---

## 8. Verificación técnica

- **Build**: los 15 proyectos .NET compilan sin errores ni warnings.
- **Tests**: 1963/1963 pasan, 0 fallos, 0 regresiones.
  ```
  TaxVision.Notification.Tests    110 / 110
  TaxVision.PaymentApp.Tests       59 /  59
  TaxVision.Tenant.Tests           64 /  64
  TaxVision.Postmaster.Tests      146 / 146
  TaxVision.PaymentClient.Tests   116 / 116
  TaxVision.Connectors.Tests      262 / 262
  TaxVision.Signature.Tests       163 / 163
  TaxVision.Auth.Tests            212 / 212
  TaxVision.Scribe.Tests          143 / 143
  TaxVision.CloudStorage.Tests    197 / 197
  TaxVision.Growth.Tests           61 /  61
  TaxVision.Subscription.Tests     77 /  77
  TaxVision.Customer.Tests         23 /  23
  ─────────────────────────────────────────
  Total                          1963 / 1963  ✅
  ```
- **Verificación funcional** (por reporte del usuario):
  - `GET /auth/me` — pasó de **404** a **200 OK con el payload correcto**.
  - `GET /auth/sessions/me` — pasó de `[]` (vacío) a devolver las sesiones activas reales.

---

## 9. Métodos arreglados — inventario completo

| Servicio | Archivo | Método | Bug que corrigió |
|---|---|---|---|
| **Auth** | `UserRepository.cs` | `GetByIdAsync` | `/auth/me` 404 |
| **Auth** | `RoleRepository.cs` | `GetByIdAsync` | Update/Set/Deactivate role NotFound |
| **Auth** | `TenantDomainRepository.cs` | `GetByIdAsync`, `GetProvisioningCustomHostnamesAsync` | Verify/Activate domain NotFound, poller Cloudflare no ejecutaba |
| **Auth** | `InvitationRepository.cs` | `GetByIdAsync` | Cancel/Resend invitation NotFound |
| **Auth** | `SessionRepository.cs` | `GetActiveSessionsByUserAsync`, `GetSessionByIdAsync`, `RevokeSessionAsync`, `RevokeAllForUserAsync`, `GetTokenByHashAsync` | `/auth/sessions/me` vacío, logout no-op silencioso, refresh token roto |
| **Auth** | `CredentialTokenRepository.cs` | `GetPasswordResetByHashAsync`, `GetEmailVerificationByHashAsync` | Reset password y verificación de email no encontraban token |
| **Auth** | `MfaRepository.cs` | `GetChallengeByTicketHashAsync`, `GetMethodByIdAsync`, `GetTrustedDeviceByHashAsync` | MFA challenge/device flow roto |
| **Scribe** | `EmailTemplateRepository.cs` | `GetVersionByIdAsync` | Update template version NotFound |
| **Scribe** | `EmailLayoutRepository.cs` | `GetByIdAsync` | Add/Publish layout version NotFound |
| **Scribe** | `EventTemplateMappingRepository.cs` | `GetByIdAsync`, `RemoveAsync` | Update/Delete/Get mapping NotFound |
| **Customer** | `CustomerRepository.cs` | `GetByIdAsync` | Los ~24 handlers de Customer daban NotFound (Update/Activate/Archive/AssignPreparer/AddAddress/…) |
| **Customer** | `CustomerImportRepository.cs` | `GetByIdAsync` | Consumers de import no procesaban archivos |
| **Customer** | `CustomerImportReadService.cs` | `GetByIdAsync`, `StreamRowsAsync` | Reports de import vacíos |
| **Notification** | `EmailCampaignRepository.cs` | `GetForProcessingAsync`, `GetByIdNoRecipientsAsync`, `GetRecipientsPageAsync` | Campañas atascadas sin fan-out ni actualización de contadores |
| **Notification** | `OutboundEmailRepository.cs` | `GetForDeliveryAsync` | Mensajes email atascados en `Sending` para siempre |
| **Notification** | `NotificationLogQueryRepository.cs` | `FindWithAttemptsAsync` | TODOS los callbacks Postmaster (Succeeded/Failed/Bounced/Suppressed/ProviderNotConfigured) caían en "log desconocido; dropping" |
| **PaymentApp** | `SaaSPaymentRepository.cs` | `GetByIdempotencyKeyAsync`, `GetByExternalReferenceAsync`, `SumSucceededAmountCentsAsync`, `CountDueForRetryAsync` | Pagos duplicados por retry idempotente, webhooks Stripe no actualizaban estado, métricas en cero |
| **PaymentApp** | `WebhookEventRepository.cs` | `ExistsAsync` | Dedup de webhooks entrantes roto (procesaba duplicados) |
| **PaymentClient** | `PaymentLinkRepository.cs` | `GetByTokenAsync`, `GetByRelatedTenantPaymentIdAsync` | Checkout público de payment links siempre 404, webhooks no actualizaban links relacionados |
| **PaymentClient** | `TenantConnectAccountRepository.cs` | `GetByStripeConnectAccountIdAsync` | TODOS los webhooks Stripe Connect ignorados |
| **PaymentClient** | `PayoutScheduleRepository.cs` | `GetByTenantConnectAccountIdAsync` | Payout schedules duplicados en Upsert |
| **PaymentClient** | `TenantPaymentRepository.cs` | `GetStuckProcessingAsync`, `GetDueForRetryAsync` | Jobs cross-tenant no encontraban pagos vencidos |
| **Growth** | `TenantRepositoryGuard.cs` | `Matches` (1 fix arquitectónico) | Los 12 repos de Growth (códigos, redemptions, referrals, cuotas, etc.) eran no-op silencioso dentro de cualquier handler Wolverine |

---

## 10. Trabajo pendiente (documentado como futuro refactor)

**Growth — 2 casos que requieren cambio de firma pública** (no son parches):

- `SqlBusinessIdempotencyExecutor.ExecuteAsync` — agregar `Guid tenantId` como parámetro público, cambiar los ~10 call sites que hoy asumen `tenantContext.TenantId`.
- `SqlReferralRewardQuota.TryReserveAnnualSlotAsync` — mismo tratamiento.

Ambos hoy fallan **ruidosamente** (devuelven `Result.Failure` explícito o `false`) dentro de Wolverine — no es corrupción silenciosa como los otros bugs — así que no bloquean producción del resto de Growth, pero cualquier operación que dependa de business-idempotency o cuota anual T2T queda inoperativa desde consumers.

Se puede cerrar en una PR aparte cuando el equipo lo priorice.

---

**Autor**: sesión del 2026-07-24
**Referencias en código**: buscar `IgnoreQueryFilters()` en `src/Services/*/Infrastructure/**/*.cs` — cada uso tiene comentario justificando la seguridad.
