# WhatsApp — Deployment

- Servicio: **TaxVision.WhatsApp** (NEW)
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**

## 1. Forma del despliegue
- Microservicio **independiente** `TaxVision.WhatsApp` (.NET, EF Core, Wolverine), con **su propia base de datos** (sin FK cross-context). Se suma al stack de contenedores existente; expuesto por el gateway (mismo patrón que los demás servicios del monorepo).
- Rol único: ejecutor del canal WhatsApp. No aloja Campaigns, Wallet ni Scheduler.
- Réplicas horizontales: seguras por diseño (idempotencia + optimistic concurrency + lease del reaper; ver `Concurrency_Spec.md`). El webhook público debe apuntar a un endpoint estable tras el gateway.

## 2. Dependencias de arranque
| Depende de | Para | Estado |
|---|---|---|
| Broker Wolverine (outbox/inbox durable) | dispatch/result, dedupe | infra existente |
| Wallet/Ledger | reserve/consume/refund | **NEW — debe existir antes de ejecutar** (dependencia dura, `05_Master_ADR.md:57`) |
| Campaigns | origen del dispatch | NEW |
| Scribe | render Fluid/Liquid de variables | REUSE |
| Meta WhatsApp Business Platform (Cloud API) | entrega + webhooks | externo |
| KMS/secret store | cifrar tokens | infra existente |

## 3. Configuración (no secreta en appsettings; secretos en store cifrado)
```
WhatsApp:
  Provider: "MetaCloudApi"
  GraphApiBaseUrl: "https://graph.facebook.com/v20.0"
  WebhookVerifyToken: <secret ref>          # no en claro
  MaxConcurrencyPerPhoneNumber: 20          # backpressure
  SentWithoutWebhookTimeout: "00:30:00"     # T_max del reaper
  TemplateSyncInterval: "01:00:00"
# AccessToken / AppSecret / WabaId / PhoneNumberId → tabla WhatsAppProviderConfigs (cifrado), NO aquí
```
El precio por mensaje **no** se configura aquí (vive en Wallet/Campaigns; el real llega por webhook). Corrige `CostSettings.WhatsAppCostPerMessage` en appsettings del legado (`appsettings.json:141`).

## 4. Onboarding operativo (prerrequisito, BLOCKER B-WA-DEP-1)
Antes de que un tenant pueda enviar:
1. WABA + Business verification en Meta (embedded signup / manual).
2. Phone Number registrado y `PhoneNumberId` guardado en `WhatsAppProviderConfigs` (cifrado).
3. Al menos una **plantilla Approved** por caso de uso/idioma/categoría.
4. Webhook suscrito (`messages`, `message_status`, `message_template_status_update`) apuntando al gateway, con `verify_token` y `AppSecret` configurados.
Sin (1)–(4) el canal responde `Rejected(PROVIDER_NOT_CONFIGURED)`.

## 5. Migraciones
- EF migrations del servicio (tablas de `Data_Model.md`) + tablas de outbox/inbox Wolverine. Deploy con migración previa a arranque (patrón del monorepo). Rollback: el servicio es aditivo (no toca datos de otros contexts).

## 6. Reaper / scheduler del timeout
Decisión en `../scheduler/ADR.md` si el reaper de `Sent`-sin-webhook corre como job del Scheduler central o como worker interno del servicio. Recomendación: **worker interno** con lease atómico (el timeout es específico del canal), coherente con el fix del doble-scheduler del legado.

## 7. Checklist de readiness
- [ ] Wallet desplegado y alcanzable (M2M).
- [ ] Broker con streams de dispatch/result/webhook.
- [ ] Secret store con tokens cifrados por tenant/plataforma.
- [ ] Webhook público registrado + firma verificada end-to-end.
- [ ] `[RateLimit]`/`[RateLimitExempt]` en todos los endpoints.
- [ ] Al menos una plantilla Approved para smoke test.

## 8. Evidencia
| Hecho | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Deployment independiente `TaxVision.WhatsApp` | `00_Overview_And_Index.md:22` | VERIFIED | 96% |
| Wallet debe existir antes de ejecutar | `05_Master_ADR.md:57` | VERIFIED | 95% |
| Precio no en el ejecutor | `02_Context_Map.md:54` | VERIFIED | 94% |
| Config appsettings de costo/proveedor legado | `appsettings.json:130-143` | VERIFIED | 95% |
| Onboarding WABA/plantillas prerrequisito | Meta Cloud API docs | DOCUMENTED_ONLY | 85% |
