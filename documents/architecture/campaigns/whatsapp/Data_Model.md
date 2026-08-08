# WhatsApp — Data Model

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
| `Category` | smallint | Marketing/Utility/Authentication (auditoría de precio) |
| `IsFreeForm` | bool | true solo dentro de sesión |
| `ProviderMessageId` | text null | `wamid`; **UNIQUE (TenantId, ProviderMessageId) WHERE not null** |
| `Status` | smallint | Pending/Accepted/Sent/Delivered/Read/Failed/Rejected |
| `ConversationId` | text null | de webhook |
| `ConversationCategory` | smallint null | de webhook pricing |
| `PricingModel` | text null | de webhook |
| `BilledAmountCents` | bigint null | Money minor units (USD) |
| `BilledCurrency` | char(3) null | ISO |
| `ReservationRef` | uuid | correlación reserva Wallet |
| `ConsumeRef` / `RefundRef` | uuid null | movimiento aplicado |
| `FailureCode` / `FailureDetail` | text null | taxonomía interna |
| `AcceptedAtUtc, SentAtUtc, DeliveredAtUtc, ReadAtUtc, FailedAtUtc` | timestamptz null | |
| `CreatedAtUtc` | timestamptz | |
| `RowVersion` | bytea/rowversion | concurrencia optimista |

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
- **Dinero en minor units (`bigint` cents) + ISO currency**; nunca `float`/`decimal` confiado del frontend (corrige `SendResult.Cost` decimal y precio de appsettings del legado).
- Sin snapshot de contacto (nombre/teléfono se resuelven vía Customer en Campaigns; aquí solo el `ToPhoneE164` ya resuelto + `RecipientRef` opaco). Corrige el snapshot stale del legado (`CampaignRecipient` copiaba Email/Phone/Name, `CampaignRecipient.cs:12-16`).
- Retención: `WhatsAppMessages` con TTL configurable (auditoría de costo); `ProcessedBusinessMessage` con `ExpiresAtUtc`.

## 3. Comparación con legado
| Legado | Nuevo |
|---|---|
| `CampaignRecipient` mutable con timestamps sueltos (`CampaignRecipient.cs:18-28`) | `WhatsAppMessage` con máquina de estado + RowVersion |
| `ChannelConfiguration Dictionary<string,string>` (`WhatsAppCampaignSender.cs:49-54`) | `WhatsAppTemplates.ComponentsSchema jsonb` tipado |
| Token Twilio plano en appsettings (`appsettings.json:132-134`) | `AccessTokenEnc/AppSecretEnc` cifrados |
| Costo decimal plano (`CostService.cs:17`) | `BilledAmountCents` desde webhook `pricing` |

## 4. Evidencia
| Hecho | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Snapshot stale de contacto en legado | `CampaignRecipient.cs:12-16` | VERIFIED | 96% |
| ChannelConfiguration sin esquema | `WhatsAppCampaignSender.cs:49-54` | VERIFIED | 97% |
| Token plano legado | `appsettings.json:130-136` | VERIFIED | 95% |
| Patrón ProcessedBusinessMessage | `ProcessedBusinessMessage.cs:9-23` | VERIFIED | 97% |
