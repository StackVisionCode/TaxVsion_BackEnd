# WhatsApp — Data Model

> **REVISIÓN 2026-09-16 (ADR-CAMP-001, APPROVED) — WhatsApp SÍ es un servicio nuevo (`TaxVision.WhatsApp`, Meta/WABA), pero de FASE POSTERIOR y solo como CONSUMER del contrato de dispatch — sin dinero.** Consume `campaign.dispatch.requested.v1` y responde `campaign.dispatch.result.v1`. Campaign es un orquestador agnóstico que **no envía**. **Sin dinero:** este doc NO reserva/consume/cobra saldo; la autorización por balance es un interceptor/PEP externo y DIFERIDO (ver `../05_Master_ADR.md` D1/D3/D7). Todo lo que abajo asuma un Wallet o cobro por este canal queda **superseded**. Canónico: `../campaigns/` + `../05_Master_ADR.md`.

- Servicio: **TaxVision.WhatsApp** (NEW)
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**
- Persistencia: EF Core, base propia del servicio (sin FK cross-context; solo IDs opacos). Multi-tenant **fail-closed**: query filter global por `TenantId` + repos tenant-scoped; escrituras cross-tenant solo con `.IgnoreQueryFilters()` + tenant explícito (ver `documents/Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md`).

## 1. Tablas

### `WhatsAppMessages` (aggregate root)
| Columna | Tipo | Nota |
|---|---|---|
| `Id` | uuid PK | |
| `TenantId` | uuid | query filter global; index |
| `DispatchId` | uuid | **UNIQUE (TenantId, DispatchId)** — idempotencia por destinatario |
| `CampaignId` | uuid null | opaco, eco; index |
| `CampaignRunId` | uuid null | run inmutable; index |
| `RecipientRef` | text | id opaco de contacto |
| `Attempt` | int | parte de la clave lógica de intento |
| `ToPhoneE164` | text | destino normalizado |
| `TemplateName` / `TemplateLanguage` | text null | HSM usado |
| `TemplateVersion` | int null | versión del catálogo local en el envío |
| `Category` | smallint | Marketing/Utility/Authentication (auditoría de plantilla/categoría; sin precio en este canal) |
| `IsFreeForm` | bool | true solo dentro de sesión |
| `ProviderMessageId` | text null | `wamid`; **UNIQUE (TenantId, ProviderMessageId) WHERE not null** |
| `Status` | smallint | Pending/Accepted/Sent/Delivered/Read/Failed/Rejected |
| `ConversationId` | text null | de webhook (modelo de conversación) |
| `ConversationCategory` | smallint null | de webhook (modelo de conversación) |
| `FailureCode` / `FailureDetail` | text null | taxonomía interna |
| `AcceptedAtUtc, SentAtUtc, DeliveredAtUtc, ReadAtUtc, FailedAtUtc` | timestamptz null | |
| `CreatedAtUtc` | timestamptz | |
| `RowVersion` | bytea/rowversion | concurrencia optimista |

> Columnas `PricingModel`, `BilledAmountCents`, `BilledCurrency`, `ReservationRef`, `ConsumeRef`/`RefundRef` — (removidas: sin dinero en el canal; ver banner).

Índices: `(TenantId, DispatchId)` unique; `(TenantId, ProviderMessageId)` unique parcial; `(TenantId, CampaignRunId, Status)` para agregados; `(TenantId, Status)` para reintentos/rezagados.

### `WhatsAppTemplates` (catálogo local espejado de Meta)
| Columna | Tipo | Nota |
|---|---|---|
| `Id` | uuid PK | |
| `TenantId` | uuid | index |
| `MetaTemplateId` | text | id en Meta |
| `Name` / `Language` | text | **UNIQUE (TenantId, Name, Language)** |
| `Category` | smallint | Marketing/Utility/Authentication |
| `Status` | smallint | Pending/Approved/Rejected/Paused/Disabled |
| `ComponentsSchema` | jsonb | esquema **tipado y versionado** (header/body/footer/buttons + placeholders) — corrige `Dictionary<string,string>` sin esquema del legado |
| `Version` | int | incrementa en cada cambio aprobado |
| `LastSyncedAtUtc` | timestamptz | |
| `RowVersion` | bytea | |

### `SessionWindows` (proyección de inbound)
| Columna | Tipo | Nota |
|---|---|---|
| `Id` | uuid PK | |
| `TenantId` | uuid | |
| `PhoneNumberId` | text | número de negocio |
| `CustomerWaId` | text | número del usuario | 
| `OpenedAtUtc` / `ExpiresAtUtc` | timestamptz | ventana 24h |
| `RowVersion` | bytea | |

Índice **UNIQUE (TenantId, PhoneNumberId, CustomerWaId)** (upsert por inbound; `ExpiresAtUtc = last_inbound + 24h`).

### `WhatsAppProviderConfigs` (secretos cifrados)
| Columna | Tipo | Nota |
|---|---|---|
| `Id` | uuid PK | |
| `TenantId` | uuid null | null = config de plataforma |
| `WabaId` / `PhoneNumberId` | text | |
| `AccessTokenEnc` | bytea | **cifrado en reposo** (envelope; ver `Security.md`) |
| `AppSecretEnc` | bytea | para verificar firma del webhook |
| `Provider` | text | `MetaCloudApi` (default) |
| `IsActive` | bool | |
| `RowVersion` | bytea | |

### `ProcessedBusinessMessages` (dedupe de efecto)
Copia local del patrón `Growth/.../Idempotency/ProcessedBusinessMessage.cs` (`Operation, ScopeId, IdempotencyKey, RequestFingerprint(SHA-256 64hex), Status, ExpiresAtUtc, RowVersion`). **UNIQUE (TenantId, Operation, ScopeId, IdempotencyKey)**.

### Infra Wolverine
Tablas de **outbox/inbox durable** del servicio (envelopes + dedupe de transporte). El inbox de Wolverine deduplica envelopes; `ProcessedBusinessMessage` deduplica efecto de negocio (dos capas distintas).

## 2. Reglas de datos
- **Dinero** — (removido: sin dinero en el canal; no hay columnas de importe/moneda; la autorización por balance es externa/diferida, ver banner).
- Sin snapshot de contacto (nombre/teléfono se resuelven vía Customer en Campaigns; aquí solo el `ToPhoneE164` ya resuelto + `RecipientRef` opaco). Corrige el snapshot stale del legado (`CampaignRecipient` copiaba Email/Phone/Name, `CampaignRecipient.cs:12-16`).
- Retención: `WhatsAppMessages` con TTL configurable (auditoría de entrega); `ProcessedBusinessMessage` con `ExpiresAtUtc`.

## 3. Comparación con legado
| Legado | Nuevo |
|---|---|
| `CampaignRecipient` mutable con timestamps sueltos (`CampaignRecipient.cs:18-28`) | `WhatsAppMessage` con máquina de estado + RowVersion |
| `ChannelConfiguration Dictionary<string,string>` (`WhatsAppCampaignSender.cs:49-54`) | `WhatsAppTemplates.ComponentsSchema jsonb` tipado |
| Token Twilio plano en appsettings (`appsettings.json:132-134`) | `AccessTokenEnc/AppSecretEnc` cifrados |

## 4. Evidencia
| Hecho | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Snapshot stale de contacto en legado | `CampaignRecipient.cs:12-16` | VERIFIED | 96% |
| ChannelConfiguration sin esquema | `WhatsAppCampaignSender.cs:49-54` | VERIFIED | 97% |
| Token plano legado | `appsettings.json:130-136` | VERIFIED | 95% |
| Patrón ProcessedBusinessMessage | `ProcessedBusinessMessage.cs:9-23` | VERIFIED | 97% |
