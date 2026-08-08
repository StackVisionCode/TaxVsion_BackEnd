# WhatsApp (TaxVision.WhatsApp) — Domain Design

- Servicio: **TaxVision.WhatsApp** (ejecutor de canal WhatsApp, NEW / greenfield)
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**
- Ancla: coherente con `../00_Overview_And_Index.md`, `../02_Context_Map.md`, `../05_Master_ADR.md` (ADR-CAMP-000).

## 1. Rol y frontera del bounded context

`TaxVision.WhatsApp` es un **ejecutor de canal**, no un creador. Recibe un contrato **dispatch por destinatario** desde Campaigns (o desde un consumidor de envío individual), **renderiza** la plantilla, **entrega** vía **WhatsApp Business Platform (Meta Cloud API)**, procesa los **webhooks de estado** (sent/delivered/read/failed), y **reporta un result** con el contrato común. No define audiencia, no agenda, no muta el saldo del Wallet, no conoce planes.

Lo que el servicio **posee**:
- El adaptador y los secretos de Meta (System Access Token, App Secret, Phone Number ID, WABA ID) **cifrados**, por tenant o por plataforma (ver `Security.md`).
- El **catálogo local de plantillas aprobadas (HSM)** espejado desde Meta (nombre, idioma, categoría, componentes, estado de aprobación) — no es la fuente de verdad (Meta lo es) pero es el índice consultable para validar un dispatch antes de gastar.
- La **máquina de estado por mensaje** (`WhatsAppMessage`) y la **ventana de sesión de 24h** por (tenant, phone number, destinatario).
- El **costeo real por conversación/plantilla** que Meta reporta, para informar a Wallet el `consume` correcto.

Lo que el servicio **NO** posee (frontera dura, ver `02_Context_Map.md §Fronteras`):
- Audiencia, schedule, stats agregadas de campaña → Campaigns.
- Saldo/movimientos → Wallet/Ledger. WhatsApp **solicita** consume/refund; **nunca** edita saldo.
- El motor de render Fluid/Liquid → **Scribe (REUSE)**. WhatsApp solo mapea variables a componentes de plantilla.
- El cobro del top-up → PaymentApp.

## 2. Diferencia estructural con el resto de canales (por qué WhatsApp es su propio contexto)

WhatsApp no es "SMS con imágenes". Impone reglas de negocio del proveedor que ningún otro canal tiene y que **deben modelarse como invariantes de dominio**, no como configuración:

| Regla de Meta | Consecuencia de dominio |
|---|---|
| Fuera de la ventana de sesión de 24h **solo** se puede iniciar con una **plantilla aprobada (HSM)** | Un dispatch de campaña (marketing/utility) es **siempre** template-first; texto libre solo cabe dentro de sesión abierta. |
| Cada plantilla tiene **categoría** (marketing / utility / authentication) | La categoría determina el **precio de conversación** y las reglas de opt-in → el costo no es plano por mensaje. |
| El envío abre/consume una **conversación** facturable (modelo Meta), migrando a **per-message pricing** (jul-2025) según categoría | El `consume` a Wallet se calcula por **categoría de plantilla**, no por un tarifa fija global (corrige el legado de 0.005 plano). |
| Los estados llegan **asíncronos por webhook** (`sent→delivered→read`, o `failed`) con `pricing`/`conversation` embebidos | El result no es síncrono al POST; hay un **avance de estado diferido** que actualiza costo real. |

## 3. Agregados y entidades

### 3.1 `WhatsAppMessage` (aggregate root)
Una fila **inmutable en identidad**, un intento de entrega por destinatario. Estado gobernado por métodos del aggregate que devuelven `Result` (nunca setters públicos; corrige el legado con propiedades mutables sueltas en `CampaignRecipient`).

Campos clave:
- `Id` (Guid, PK interna)
- `TenantId` (fail-closed, query filter global)
- `DispatchId` — clave opaca del contrato dispatch común (idempotencia por destinatario).
- `CampaignId?` — correlación opaca **transportada de ida y vuelta, nunca interpretada** (mismo patrón que `NotificationsEmailSendRequestedIntegrationEvent.CampaignId`, `src/BuildingBlocks/Messaging/EmailIntegrationEvents/PostmasterEmailEvents.cs:37`). Null para envío individual.
- `CampaignRunId?` — run inmutable que originó (si vino de Campaigns).
- `RecipientRef` — id opaco del contacto (no snapshot stale; el número viaja en el dispatch ya resuelto por Campaigns vía Customer).
- `ToPhoneE164` — destino normalizado E.164 (ver `FormatWhatsAppNumber` legado hardcodeaba +1 RD, `WhatsAppCampaignSender.cs:136-140` — **anti-patrón a corregir**: normalización debe usar país explícito del contacto, no default).
- `TemplateRef` (nombre + idioma + versión de la plantilla usada) o `SessionFreeText` (solo si sesión abierta).
- `Category` (Marketing | Utility | Authentication) — copiada de la plantilla en el momento del dispatch (auditoría de precio).
- `ProviderMessageId?` — `wamid.*` que devuelve Meta (reemplaza el `Guid.NewGuid()` simulado del legado, `WhatsAppCampaignSender.cs:99`).
- `Status` (ver `State_Machines.md`).
- `ConversationId?`, `ConversationCategory?`, `PricingModel?`, `BilledAmount` (Money, minor units) — poblados desde el webhook.
- `ReservationRef` / `ConsumeRef` — correlación con el movimiento Wallet.
- `Attempt` (int) — número de intento; la triple `(CampaignId, RecipientRef, Attempt)` es la clave de idempotencia de dispatch (ver `Idempotency_Spec.md`).
- `RowVersion` (concurrencia optimista).
- Timestamps: `AcceptedAtUtc, SentAtUtc, DeliveredAtUtc, ReadAtUtc, FailedAtUtc`.
- `FailureCode?`, `FailureDetail?` (mapeo del error de Meta a taxonomía interna).

### 3.2 `WhatsAppTemplate` (entidad, catálogo local espejado)
- `Id`, `TenantId`, `Name`, `Language`, `Category`, `Status` (`Pending|Approved|Rejected|Paused|Disabled`), `ComponentsSchema` (JSON tipado: header/body/footer/buttons con placeholders `{{1}}..{{n}}`), `MetaTemplateId`, `LastSyncedAtUtc`, `Version`.
- **No** es un `Dictionary<string,string>` sin esquema (corrige `ChannelConfiguration` del legado, `WhatsAppCampaignSender.cs:49-54`): los componentes son un contrato tipado y versionado.
- Poblado por sync desde Meta (Graph API `message_templates`) + webhook `message_template_status_update`.

### 3.3 `SessionWindow` (entidad / proyección)
- `(TenantId, PhoneNumberId, CustomerWaId)` → `OpenedAtUtc`, `ExpiresAtUtc` (= último inbound del usuario + 24h).
- Derivada de webhooks inbound (`messages` entrantes del usuario). Determina si un mensaje **free-form** es admisible o si debe forzarse plantilla.

### 3.4 `WhatsAppProviderConfig` (entidad, secretos cifrados)
- `(TenantId | Platform)`, `PhoneNumberId`, `WabaId`, `AccessTokenEnc`, `AppSecretEnc`, `Provider` (Meta Cloud API por defecto). Cifrado en reposo (ver `Security.md`). **Nunca** en texto plano (corrige `WhatsAppProvider.AuthToken` legado en appsettings, `appsettings.json:130-136`, y el patrón `SmtpProviderConfig.ApiKey` plano del legado).

## 4. Lenguaje ubicuo (delta específico WhatsApp)

| Término | Definición |
|---|---|
| HSM / Plantilla | Highly Structured Message pre-aprobado por Meta; obligatorio fuera de sesión. |
| Ventana de sesión | 24h desde el último inbound del usuario; dentro se permite free-form. |
| Categoría | Marketing / Utility / Authentication; fija el precio de conversación y el opt-in requerido. |
| Conversación | Unidad facturable de Meta (hilo 24h por categoría); base del costeo real. |
| `wamid` | Id de mensaje que Meta asigna; nuestra `ProviderMessageId`. |
| Dispatch / Result | Contrato común entrante/saliente por destinatario (idéntico a los otros canales). |

## 5. Evidencia

| Hecho | Evidencia (file:line) | Clasificación | Confianza |
|---|---|---|---|
| Backend WhatsApp nuevo no existe (solo stub simulado legado) | `WhatsAppCampaignSender.cs:77-101` (Task.Delay + Guid falso) | VERIFIED | 97% |
| Legado usa Twilio, no Cloud API nativa, token plano | `appsettings.json:130-136` (`Provider:"Twilio"`, `AuthToken`) | VERIFIED | 95% |
| Legado sin plantilla/sesión/webhook/categoría | `WhatsAppCampaignSender.cs` (no hay ninguno) | VERIFIED | 95% |
| Legado costo plano 0.005/0.01 (no por conversación) | `CostService.cs:17`, `appsettings.json:141` | VERIFIED | 96% |
| ChannelConfiguration sin esquema | `WhatsAppCampaignSender.cs:49-54` | VERIFIED | 97% |
| Normalización de número hardcodea país | `WhatsAppCampaignSender.cs:136-140` | VERIFIED | 96% |
| Seam CampaignId opaco reutilizable | `PostmasterEmailEvents.cs:37` | VERIFIED | 95% |
| Modelo de conversación/categoría/24h/webhook | Documentación pública Meta WhatsApp Business Platform | DOCUMENTED_ONLY | 88% |
| Dedupe de efecto de negocio disponible | `ProcessedBusinessMessage.cs` (Begin/Complete/Fail) | VERIFIED | 97% |

## 6. Blockers de dominio
- **B-WA-DOM-1**: Onboarding de WABA (embedded signup Meta) por tenant no existe; sin un WABA + Phone Number + plantillas aprobadas, el canal no puede enviar. Prerrequisito operativo (ver `Deployment.md`).
- **B-WA-DOM-2**: El precio por categoría/país de Meta cambia (migración a per-message jul-2025). El costeo debe leerse del webhook (`pricing`), no cablearse. Ver `Transactional_Protocol.md §Costeo`.
