# Campaigns Suite — Frontend MVP (pantalla mínima Email/SMS)

> **Alcance (review #27):** una UI mínima para el piloto **Email+SMS** — crear, revisar audiencia/remitente, enviar, ver estado y cancelar — **antes** de terminar Push/WhatsApp. Alineada al modelo v2 (`campaigns/State_Machines.md`): estados `Accepted/Delivered/Unknown/Skipped`, unidad = destinatario/canal, **sin dinero**.

- **Stack:** `TaxVsion_Front` (Angular), feature `features/campaigns` (ver memoria `project_frontend_billing_apartado` para el patrón de otra feature real).
- **Acceso:** ruta protegida por **`campaigns.manage`** (route guard sobre el permiso ya cableado, `PermissionCatalog.cs:39`). Si el tenant **no** tiene el entitlement `module.campaigns` → mostrar estado "feature no disponible" (upsell), no un 500. Los endpoints devuelven `403 feature_not_enabled`.
- **Base API:** gateway `/campaigns/**` (mismo patrón que `/billing`, `/sms`). Tenant del JWT; nunca se envía tenant desde el cliente.

---

## 1. Pantallas (mínimo viable)

| # | Pantalla | Contenido | Acciones |
|---|---|---|---|
| 1 | **Lista de campañas** | nombre, canal(es), estado (`Draft/Ready/Scheduled/Archived`), última ejecución + contadores resumidos | Crear · abrir · archivar |
| 2 | **Editar campaña (Draft)** | nombre; **canales** (Email/SMS multi); **remitente por canal** (selector de `SenderProfile` **Verified**); **contenido por canal** (Email: plantilla Scribe + asunto; SMS: texto); **audiencia** (Clients/segmento + Listas propias + Manual); **modo** (Immediate/Scheduled/Recurring + TZ). Indicador de **readiness** ("qué falta para enviar"). | Guardar · Enviar ahora · Agendar |
| 3 | **Detalle de run** | snapshot; `recipientCount` (unidades); **contadores por canal**: Dispatched/Accepted/Delivered/Failed/Skipped/Unknown; barra de progreso | Cancelar run · refrescar |
| 4 | **Drill-down destinatarios** | tabla paginada por unidad (destinatario × canal): `dispatchState`, `reason`, `providerRef`, intentos; filtros por estado/canal | exportar |
| 5 | **Contactos / Listas** | CRUD, **import CSV** (resumen creados/duplicados/inválidos), **opt-out** por canal | crear/editar/importar/opt-out |
| 6 | **Remitentes** | CRUD `SenderProfile` por canal + **estado de verificación** (Pending/Verified/Disabled) | crear/editar/deshabilitar |

---

## 2. Copy honesto de estados (fix #03 en la UI)

La UI **no** debe decir "Entregado" cuando el backend solo confirmó aceptación. Mapa de etiquetas:

| Estado backend | Etiqueta UI | Tooltip |
|---|---|---|
| `Accepted` | "Aceptado por el proveedor" | "El proveedor lo aceptó; la entrega se confirma después." |
| `Delivered` | "Entregado" | "Entrega confirmada." |
| `Failed` | "Falló" | muestra `reason`. |
| `Skipped` | "Omitido" | muestra `reason` (opt-out / sin destino / remitente / frequency_cap…). |
| `Unknown` | "Sin confirmación" | "No llegó confirmación a tiempo; se reconcilia — no reenviar aún." |

---

## 3. Envío, idempotencia y cancelación

- **Enviar ahora / Agendar:** el cliente genera un `Idempotency-Key` (uuid) por intento de disparo; reintentar el mismo botón/clic **no** crea dos runs (mismo key → mismo run). Un "enviar de nuevo" deliberado usa key nueva.
- **Respuesta:** `202 { runId, status: "Materializing" }` → navegar al detalle del run.
- **Cancelar run:** `POST /campaigns/runs/{runId}/cancel`. La UI **advierte el límite real** (fix #11): "Detiene lo aún no enviado; no revoca lo ya entregado." No prometer revocación total.
- **Errores:** `403` (sin permiso) → ocultar acción; `403 feature_not_enabled` → upsell; `409` (estado/idempotencia) → mensaje claro; readiness incompleto → deshabilitar "Enviar".

---

## 4. Tiempo real (sin polling agresivo)

- Reusar el relay **Communication/Socket.IO** existente (`communication-realtime.service.ts` en el front; el Node `Communication` retransmite eventos .NET al navegador vía `emitToTenant`/`emitToUser`). Suscribir el detalle de run a los eventos `campaign.run.*` / actualizaciones de contadores para refrescar el progreso **sin** polling continuo.
- **Fallback:** si el canal realtime no está disponible, `GET /campaigns/runs/{runId}` con backoff. Nunca dependencia dura del realtime (degradación).

---

## 5. Qué NO hace la UI (fronteras)

- **No** muestra saldo, costo ni cobro (Campaign es sin dinero; el PEP/Wallet son externos y diferidos, `05_Master_ADR.md` D1/D7).
- **No** envía tenant ni precios desde el cliente; **no** maneja secretos de proveedor (viven en el ejecutor).
- **No** renderiza la plantilla final (eso es Scribe en el ejecutor); solo edita la **referencia**/variables.

---

## 6. Orden sugerido de construcción

1. Lista + Crear/editar Draft (canales Email+SMS, remitente, contenido, audiencia).
2. Enviar ahora + Detalle de run con contadores y estados honestos.
3. Drill-down destinatarios + Cancelar.
4. Contactos/Listas + import + opt-out; Remitentes.
5. Agendar/recurrente (cuando el Scheduler esté) + realtime.

---

## 7. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Permiso `campaigns.manage` ya existe (guard de ruta) | `PermissionCatalog.cs:39,474-482` | VERIFIED | 96% |
| Relay realtime Socket.IO existe (Communication) | `communication-realtime.service.ts` (front), Communication (Node) | VERIFIED | 90% |
| Patrón de feature Angular contra backend real | memoria `project_frontend_billing_apartado` (`/billing`) | DOCUMENTED_ONLY | 88% |
| Estados/pantallas/flow propuestos | diseño (este doc), modelo v2 | NEW | 84% |
