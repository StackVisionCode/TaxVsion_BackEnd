# WhatsApp — Concurrency Spec

- Servicio: **TaxVision.WhatsApp** (NEW)
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**

## 1. Fuentes de concurrencia
1. **Webhooks concurrentes** de Meta sobre el mismo `wamid` (delivered + read casi simultáneos, o reenvíos).
2. **Reaper de timeout** vs **webhook tardío** compitiendo por el settlement de un `DispatchId`.
3. **Escalado horizontal** del servicio (N réplicas consumiendo el mismo stream de dispatch/webhook).
4. **Sync de plantilla** concurrente con dispatch que la referencia.
5. **Upsert de `SessionWindow`** por inbounds simultáneos del mismo usuario.

## 2. Mecanismos

### 2.1 Optimistic concurrency (`RowVersion`)
Todo aggregate (`WhatsAppMessage`, `WhatsAppTemplate`, `SessionWindow`, `ProviderConfig`) lleva `RowVersion`. Avances de estado hacen `UPDATE ... WHERE RowVersion=@v`; conflicto ⇒ reintento del handler (Wolverine) que **re-lee** y re-evalúa el guard monotónico (un `delivered` que perdió la carrera contra `read` simplemente no retrocede). No hay locks pesimistas en el hot path de envío.

### 2.2 Guards monotónicos de estado
El avance `Sent→Delivered→Read` es idempotente y no-decreciente (ver `State_Machines.md §1`). Dos webhooks concurrentes que intentan `Delivered` → uno gana por RowVersion, el otro re-lee y ve que ya está ≥Delivered → no-op. Elimina la necesidad de serializar webhooks.

### 2.3 Settlement excluyente
`consume` XOR `refund` por `DispatchId` mediante `ProcessedBusinessMessage(op="wa.settle", scope=DispatchId)` con **UNIQUE constraint**: el primer `Begin` gana; el segundo colisiona en la unique y reconcilia en vez de duplicar dinero. Es la barrera dura contra doble-cobro/doble-refund bajo carrera reaper↔webhook.

### 2.4 Reaper de rezagados (fix del `Status=Sending` no-atómico del legado)
Un job (owned por Scheduler o por este servicio, ver `Deployment.md`) toma mensajes `Sent` sin webhook tras `T_max` con **lease atómico** (`UPDATE ... SET LeaseUntil=now()+ttl WHERE Status=Sent AND (LeaseUntil IS NULL OR LeaseUntil<now()) RETURNING ...`). Solo el que gana el lease marca `Failed(timeout)`+refund. Corrige el doble-scheduler y el `Status=Sending` global no-atómico (anti-patrón §6 ADR-CAMP-000; `CampaignStatus.cs:6`).

### 2.5 Idempotencia de POST bajo reintento
El upsert por `DispatchId` + `wamid` UNIQUE (`Idempotency_Spec.md §2`) impide que dos réplicas que procesan el mismo `WhatsAppDispatchRequested` (at-least-once) envíen dos WhatsApps.

### 2.6 `SessionWindow` upsert
Inbounds concurrentes hacen `INSERT ... ON CONFLICT (TenantId,PhoneNumberId,CustomerWaId) DO UPDATE SET ExpiresAtUtc=GREATEST(excluded, current)` — la ventana siempre toma el máximo (último inbound), sin lost update.

## 3. Backpressure y rate limits de Meta
- Meta impone throughput por número (tiers de mensajería) y puede devolver `131056`/`80007` (rate limit). El consumidor de envío aplica **backpressure** (cola con concurrencia limitada por `PhoneNumberId`, no `Task.Run` sin límite) y **retry con backoff** para errores retriables; corrige el `Task.Delay` entre mensajes en loop del legado (anti-patrón §2). Errores no-retriables (plantilla pausada, número inválido) ⇒ `Failed` inmediato sin reintentar.
- El fan-out por destinatario ocurre en Campaigns; aquí se procesa un dispatch a la vez por mensaje, con paralelismo controlado por partición de `PhoneNumberId` (evita exceder el rate del número).

## 4. Tenant scoping bajo concurrencia
Handlers Wolverine corren con **tenant explícito en el scope** + query filter global fail-closed; escrituras cross-tenant (webhook que llega sin contexto de tenant) resuelven el tenant por `PhoneNumberId→ProviderConfig` y luego `.IgnoreQueryFilters()` con tenant explícito (ver `documents/Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md`). Nunca `.Where(TenantId==)` manual (anti-patrón §9).

## 5. Evidencia
| Hecho | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado `Status=Sending` no-atómico | `CampaignStatus.cs:6`; ADR §6 `05_Master_ADR.md:49` | VERIFIED | 96% |
| Legado fan-out `Task.Delay`/loop | ADR §2 `05_Master_ADR.md:45`; `WhatsAppCampaignSender.cs:78` | VERIFIED | 94% |
| Lease atómico como fix aprobado | `05_Master_ADR.md:30` (decisión 4) | VERIFIED | 95% |
| Guía IgnoreQueryFilters/tenant scope | `documents/Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md` | VERIFIED | 90% |
| Rate tiers / errores de Meta | Meta Cloud API docs | DOCUMENTED_ONLY | 84% |
