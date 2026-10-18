# Seat Slot Gating Determinista

Determinismo de qué empleado queda bloqueado cuando el cupo efectivo baja (p. ej. un add-on seat suspendido por impago). Reemplaza el modelo manual `assign/unassign`.

- Dominio: `TaxVision.Auth` (dueño del aggregate `User.SlotPriorityAt`).
- Gate runtime: `BuildingBlocks.Web.Session.SeatSlotGateMiddleware` (vive en el Gateway).
- Lectura rápida: tabla `TenantSeatSlotProjections` en Auth DB.
- Feature flag: `Seats:SlotGating:Mode` (`Disabled | Shadow | Enforce`).
- Código de error cliente: `seats.user_slot_gated` (HTTP 403, Problem Details JSON).

---

## Modelo mental: sillas numeradas

Cada tenant tiene N sillas (`effectiveCap = basePlanSeats + sum(addOnSeats WHERE Status==Active)`). Los empleados activos ocupan sillas en este orden:

```
(IsOwner DESC, SlotPriorityAt ASC NULLS LAST, Id ASC)
```

- Owner = silla 1 (`SlotPriorityAt = DateTime.MinValue`, nunca gated).
- Resto por `SlotPriorityAt` (fecha en la que activaron su silla).
- `SlotIndex > effectiveCap` ⇒ `IsGated = true`.

Reasignar no es "mover al slot X": es desactivar/reactivar al empleado (vuelve al final) o un override admin audit-logged (`PATCH /auth/admin/users/{id}/slot-priority`).

Portal clientes (`ActorType.CustomerPortal`) nunca ocupa silla ni se gatea. Owner (`IsOwner=true`) nunca se gatea.

---

## Diagrama de flujo

```
┌─────────────────────────┐
│ Fuente del cambio       │
│ (Auth o Subscription)   │
└────────────┬────────────┘
             │  IntegrationEvent (ver §3.3 del PLAN)
             ▼
┌─────────────────────────┐        TenantEntitlementsChanged
│ Rabbit / Wolverine      │◀──────  SubscriptionSeatSuspended
│                         │        SubscriptionSeatReactivated
│                         │        TenantPlanLimitsUpdated
│                         │        UserRegistered / Activated /
│                         │        Deactivated / Removed /
│                         │        SlotPriorityOverridden
└────────────┬────────────┘
             │
             ▼
┌───────────────────────────────────────────────────┐
│ RecomputeTenantSeatSlotsConsumer (Auth)           │
│   1. Lee users activos del tenant                 │
│   2. Ordena (IsOwner, SlotPriorityAt, Id)         │
│   3. Calcula SlotIndex 1..N, IsGated              │
│   4. Compara contra projection previa             │
│   5. Escribe TenantSeatSlotProjections (txn)      │
│   6. Emite UserSlotGated / UserSlotUnlocked       │
└────────────┬──────────────────────────────────────┘
             │
      ┌──────┴──────────────────────┐
      ▼                             ▼
┌─────────────────┐         ┌───────────────────────┐
│ UserSlotUnlocked│         │ UserSlotGated         │
│  → bump         │         │  → email TenantAdmin  │
│    Permissions  │         │    (dedupe 24 h)      │
│    Version      │         └───────────────────────┘
└─────────────────┘

                 ─── runtime ───

Request HTTP → Caddy → Gateway
    │
    ├─ UseAuthentication (JWT validado, claims cargadas)
    ├─ SeatSlotGateMiddleware   ◀── lee TenantSeatSlotProjections (ISeatSlotGate)
    │    │
    │    ├─ Mode=Disabled o exento → pass-through (0 queries)
    │    ├─ Mode=Shadow + gated   → log + métrica + sigue (200)
    │    └─ Mode=Enforce + gated  → 403 seats.user_slot_gated
    │
    ├─ UseRateLimiter
    ├─ UseAuthorization
    └─ YARP → microservicio
```

Exenciones del middleware (`SeatSlotGateMiddleware.IsExempt`):

- `[AllowAnonymous]`.
- `ActorType` ∈ `CustomerPortal`, `PlatformAdmin`, `Service` (M2M).
- Claim `IsOwner = true`.
- Rutas `/auth/me`, `/auth/logout`, `/health`, `/internal`.

---

## Ejemplo end-to-end: tenant con cap=2, 3 users

Estado inicial (Starter 2 seats, add-on seat Active = 1, total cap=3):

| User       | IsOwner | SlotPriorityAt       | SlotIndex | IsGated |
|------------|---------|----------------------|-----------|---------|
| Alice (owner) | true  | 0001-01-01           | 1         | false   |
| Bob        | false   | 2026-03-15 10:00:00  | 2         | false   |
| Carla      | false   | 2026-07-02 14:22:00  | 3         | false   |

Impago del add-on → `SubscriptionSeatSuspendedIntegrationEvent` → cap baja a 2.

Resultado tras recompute (misma transacción que escribe la projection):

| User   | SlotIndex | IsGated | Evento emitido             |
|--------|-----------|---------|----------------------------|
| Alice  | 1         | false   | —                          |
| Bob    | 2         | false   | —                          |
| Carla  | 3         | **true** | `UserSlotGated(…)` → email a TenantAdmin |

Qué ve cada actor:

- **Carla** (gated): próximo request (que no sea exento) → `403` con body:
  ```json
  {
    "error": "seats.user_slot_gated",
    "message": "Your seat is suspended due to unpaid add-on. Ask the account owner to pay or reorder slots.",
    "slotIndex": 3,
    "effectiveCap": 2,
    "contactAdminUrl": "https://<tenantHost>/account/users"
  }
  ```
  El CRM intercepta el 403 y la lleva a `/blocked/seat-suspended`. `/auth/me` y `/auth/logout` siguen funcionando.
- **Alice** (owner / admin): en `account/pages/users` ve a Carla con badge "Gated" + acción "Move up in order". Panel over-cap muestra 1 empleado bloqueado. Si paga el add-on → `SubscriptionSeatReactivated` → recompute → `UserSlotUnlocked` → bump `PermissionsVersion` → siguiente request de Carla pasa (200).
- **Admin querría promover a Carla antes que a Bob**: `PATCH /auth/admin/users/{carlaId}/slot-priority` con body `{"slotPriorityAt":"2026-02-01T00:00:00Z","reason":"Senior lead"}` → Carla pasa a slot 2, Bob queda gated hasta pagar el add-on o liberar silla.

---

## Modos y rollout

| Mode     | Lee projection | Bloquea | Email | Uso                                           |
|----------|----------------|---------|-------|-----------------------------------------------|
| Disabled | No             | No      | No    | Default. Pre-rollout o kill-switch definitivo. |
| Shadow   | Sí             | **No** (200 siempre) | No (default) | Verificación en prod sin impacto. Métrica `seats.slot_gate.decisions_total{outcome=gated}` y header dev `X-Seat-Slot-Gated`. |
| Enforce  | Sí             | Sí (403) | Sí (si `EmitNotifications=true`) | Producción con el gate activo. |

Rollout canónico (SG-9 → SG-10):

1. Deploy backend con `Mode=Disabled`. Verificar backfill:
   ```sql
   SELECT COUNT(*) FROM Users
   WHERE IsActive=1
     AND ActorType IN ('TenantEmployee','TenantAdmin')
     AND SlotPriorityAt IS NULL;   -- debe ser 0
   ```
2. Flip a `Mode=Shadow`, `EmitNotifications=false`. Monitorear 48 h: `seats.slot_gate.gated_users` por tenant debe coincidir con over-cap conocido.
3. Para cada tenant piloto, correr `GET /auth/admin/tenants/me/seat-slots` y validar el orden con negocio. Correcciones vía `PATCH /…/slot-priority` con `reason` documentado.
4. Comunicación 72 h a cada TenantAdmin.
5. Flip a `Mode=Enforce`, `EmitNotifications=true`. Monitoreo 24 h intensivo.
6. Si incidente: `Mode=Shadow` (o `Disabled`) sin redeploy. Post-mortem.

---

## Kill-switch sin redeploy

El middleware usa `IOptionsMonitor<SlotGatingOptions>`. Cambiar la clave `Seats:SlotGating:Mode` en `appsettings.json` del Gateway (o en el config map / secreto si aplica) y guardar: `IOptionsMonitor` dispara reload y la próxima request del hot path lee el modo nuevo.

- **Tráfico en vuelo**: las requests ya dentro del middleware terminan con el modo que leyeron al entrar (`options.CurrentValue.Mode` se evalúa una vez por request). No hay drift ni requests "mixed-mode".
- **No requiere reiniciar** el proceso del Gateway.
- Para bajar `Enforce → Shadow`: 403s existentes ya se enviaron; a partir del flip, nuevas requests gated reciben 200 con header dev.
- Para bajar `Shadow → Disabled`: deja de consultar la projection (0 queries en el hot path).

Verificar el flip:

```bash
# Debe aparecer en los logs del Gateway al próximo request gated:
# "Seat slot gated tenant {TenantId} user {UserId} … in Shadow mode."
```

**Lo que NO cambia con el kill-switch**: la projection sigue computándose en cada evento (no está detrás del flag). Correcto por diseño: cuando vuelva a `Enforce`, los datos están frescos.

---

## Troubleshooting

**"¿Por qué este user quedó gated?"**

```sql
SELECT TenantId, UserId, SlotIndex, EffectiveCap, IsGated, ComputedAtUtc, RevisionNumber
FROM TenantSeatSlotProjections
WHERE TenantId = @tenantId AND UserId = @userId;
```

- `IsGated=1` + `SlotIndex > EffectiveCap` ⇒ está fuera del cupo. Causa raíz: cap bajó (add-on suspendido o plan cambiado) o entraron users con prioridad anterior.
- `IsGated=1` + `IsOwner`: BUG. Fitness `SeatSlotGatingFitnessTests.Owner_is_never_gated_across_random_tenants` debe fallar. Abrir incidente.
- `ComputedAtUtc` muy viejo (> 10 min) ⇒ health check `seats-slot-projection` debería estar failing. Revisar consumer `RecomputeTenantSeatSlotsConsumer` en Rabbit.

**"¿Cómo re-ordenar manualmente?"**

```http
PATCH /auth/admin/users/{userId}/slot-priority
Authorization: Bearer <TenantAdmin>
Content-Type: application/json

{ "slotPriorityAt": "2026-01-15T10:00:00Z", "reason": "Senior tax lead moved up" }
```

- `null` manda al final (igual que una reactivación).
- `reason` obligatorio (400 si vacío).
- Permiso: `seats.slot_gating.manage` (solo TenantAdmin).
- Rate-limit: 20/min por tenant.

**"El user pagó y sigue gated"**

1. Verificar que llegó el `SubscriptionSeatReactivatedIntegrationEvent` (Rabbit UI o logs del consumer en Auth).
2. Verificar que la projection se actualizó (`ComputedAtUtc` posterior al pago).
3. Verificar que el user tenga `PermissionsVersion` bumpeado — su token JWT debería renovarse por `/auth/me/access`. Si el cliente sigue con el token viejo: logout + login o esperar al refresh.

**Métricas útiles (Observability)**

- `seats.slot_gate.decisions_total{tenant_id, mode, outcome}` — tráfico por decisión.
- `seats.slot_gate.gated_users{tenant_id}` — gauge; subidas abruptas = incidente de billing.
- `seats.slot_gate.effective_cap{tenant_id}` — gauge; comparar contra `TenantPlanLimits.MaxUsers`.

---

## Rollback de emergencia

Si Enforce dispara incidente (p. ej. owner identificado mal, projection stale, 403s masivos):

1. **Flip inmediato** `Seats:SlotGating:Mode = Disabled` (preferido) o `Shadow` (si querés métricas) en el config del Gateway. Guardar. No requiere redeploy ni restart.
2. **Verificar** que los 403s cesaron:
   ```
   seats.slot_gate.decisions_total{outcome=gated} con tasa 0 tras el flip.
   ```
3. **Comunicar** al tenant impactado por el canal estándar.
4. **Diagnosticar** antes de volver a `Enforce`:
   - ¿`SlotPriorityAt` de owner correcto? (query sobre `Users` por `TenantOwner` role).
   - ¿Projection consistente con users activos y cap efectivo?
   - ¿Backfill terminó limpio? `WHERE IsActive=1 AND ActorType IN ('TenantEmployee','TenantAdmin') AND SlotPriorityAt IS NULL` debe ser 0.
5. **Post-mortem** (blameless) + fix + fitness nuevo si corresponde + re-rollout por fases.

Rollback total (último recurso, requiere redeploy):

- La proyección y columnas son aditivas: no hay que revertir migraciones.
- Dejar `Mode=Disabled` indefinidamente es seguro — el sistema se comporta como antes del gate (over-cap banner solamente).

---

## Referencias de código

- Aggregate + eventos: `src/Services/Auth/Domain/Users/User.cs`.
- Calculator (pure): `src/Services/Auth/Application/SlotGating/TenantSeatSlotCalculator.cs`.
- Recompute consumer: `src/Services/Auth/Application/SlotGating/Consumers/RecomputeTenantSeatSlotsConsumer.cs`.
- Projection + repo: `src/Services/Auth/Infrastructure/Persistence/Projections/`.
- Middleware: `src/BuildingBlocks/BuildingBlocks.Web/Session/SeatSlotGateMiddleware.cs`.
- Gate SQL: `src/BuildingBlocks/BuildingBlocks.Infrastructure/SlotGating/SqlSeatSlotGate.cs`.
- Options: `src/BuildingBlocks/Authorization/SlotGating/SlotGatingOptions.cs`.
- Admin API: `src/Services/Auth/Api/Controllers/Admin/UserSlotPriorityController.cs`.
- Backfill hosted service: `src/Services/Auth/Api/Bootstrap/SeatSlotProjectionBackfillJob.cs`.
- Fitness tests: `deploy/tests/TaxVision.Auth.Tests/SlotGating/SeatSlotGatingFitnessTests.cs`.
- Plan maestro: `DEVS/Futuras Implementaciones/Seat-Slot-Gating-Determinista-PLAN.md` (fuera del repo).

**Pendiente conocido**: override por `TenantSetting("seats.slot_gating.mode")` (§3.4 del plan). No existe abstracción cross-service de settings K/V por tenant hoy; el kill-switch global cubre el caso operacional.
