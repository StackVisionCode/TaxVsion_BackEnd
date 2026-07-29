# TaxVision PayFlow — Plan de Implementación Completo

> **Documento maestro.** Debe leerse al inicio de cada fase antes de tocar código. Todas las decisiones arquitectónicas, contratos, prompts de implementación y criterios de aceptación viven aquí.

---

## Índice

- [Parte I — Fundamentos](#parte-i--fundamentos)
  - [1. Contexto y objetivo](#1-contexto-y-objetivo)
  - [2. Bibliografía y patrones aplicados](#2-bibliografía-y-patrones-aplicados)
  - [3. Decisiones arquitectónicas fundacionales](#3-decisiones-arquitectónicas-fundacionales)
  - [4. Coexistencia de los 3 tokens](#4-coexistencia-de-los-3-tokens)
  - [5. Bounded context y responsabilidades por servicio](#5-bounded-context-y-responsabilidades-por-servicio)
  - [6. Máquina de estados (12 estados + 8 pasos)](#6-máquina-de-estados-12-estados--8-pasos)
  - [7. Modelo de fallas y compensaciones](#7-modelo-de-fallas-y-compensaciones)
  - [8. Formato de prompts de implementación](#8-formato-de-prompts-de-implementación)
- [Parte II — Fases de implementación](#parte-ii--fases-de-implementación)
  - [Fase 0 — Baseline snapshot](#fase-0--baseline-snapshot)
  - [Fase 1 — Extracción formal del flujo](#fase-1--extracción-formal-del-flujo)
  - [Fase 2 — Matriz comparativa actual vs nuevo](#fase-2--matriz-comparativa-actual-vs-nuevo)
  - [Fase 3 — Auth: scaffolding del módulo Onboarding](#fase-3--auth-scaffolding-del-módulo-onboarding)
  - [Fase 4 — Auth: TenantOnboarding aggregate + estados](#fase-4--auth-tenantonboarding-aggregate--estados)
  - [Fase 5 — Auth: EmailVerificationChallenge (OTP)](#fase-5--auth-emailverificationchallenge-otp)
  - [Fase 6 — Auth: TermsVersion + retrofit TenantTermsAcceptance](#fase-6--auth-termsversion--retrofit-tenanttermsacceptance)
  - [Fase 7 — Auth: fitness function tests](#fase-7--auth-fitness-function-tests)
  - [Fase 8 — PaymentApp: OnboardingInitial + endpoint checkout](#fase-8--paymentapp-onboardinginitial--endpoint-checkout)
  - [Fase 9 — Auth: consumer + generación RegistrationToken](#fase-9--auth-consumer--generación-registrationtoken)
  - [Fase 10 — Documents: OnboardingReceipt DocumentType](#fase-10--documents-onboardingreceipt-documenttype)
  - [Fase 11 — Auth: cliente M2M a Documents](#fase-11--auth-cliente-m2m-a-documents)
  - [Fase 12 — Scribe + Notification: 2 templates + consumers](#fase-12--scribe--notification-2-templates--consumers)
  - [Fase 13 — Auth: endpoints finales del registro](#fase-13--auth-endpoints-finales-del-registro)
  - [Fase 14 — Auth: SubdomainReservation en módulo Onboarding](#fase-14--auth-subdomainreservation-en-módulo-onboarding)
  - [Fase 15 — Auth: Wolverine Saga (Process Manager)](#fase-15--auth-wolverine-saga-process-manager)
  - [Fase 16 — Tenant + Subscription: endpoints M2M](#fase-16--tenant--subscription-endpoints-m2m)
  - [Fase 17 — Compensaciones + ManualReview + observabilidad](#fase-17--compensaciones--manualreview--observabilidad)
  - [Fase 18 — Credentials Hardening (Forgot Password + Refresh + Invitation)](#fase-18--credentials-hardening-forgot-password--refresh--invitation)
  - [Fase 19 — README + Postman + verificación E2E](#fase-19--readme--postman--verificación-e2e)
- [Parte III — Anexos](#parte-iii--anexos)
  - [Anexo A — Checklist final de validación](#anexo-a--checklist-final-de-validación)
  - [Anexo B — Matriz de fallas y recuperación](#anexo-b--matriz-de-fallas-y-recuperación)
  - [Anexo C — Glosario](#anexo-c--glosario)
  - [Anexo D — Referencias a archivos y líneas del repo](#anexo-d--referencias-a-archivos-y-líneas-del-repo)

---

# Parte I — Fundamentos

## 1. Contexto y objetivo

TaxVision es un SaaS multi-tenant. El flujo actual de alta de tenants es "PlatformAdmin crea tenant → Auth invita al TenantAdmin → suscripción arranca en Trial → el admin puede activar y pagar cuando quiera". **No existe** un flujo self-service de "pago primero → creación de tenant después".

El nuevo flujo (documentado en `Implementaciones/PayFlowNew/Tenant_Onboarding_Flujo_Seguro_Arquitectura.pdf` + `flowpay.png`, 40 pasos) invierte el orden: **pago primero, provisioning post-pago vía Saga, cliente jamás paga dos veces**. Introduce OTP de email, RegistrationToken opaco, 12 estados persistidos, 8 pasos de provisioning con compensaciones explícitas, y evidencia legal fuerte de aceptación de términos.

**Objetivo de este plan**: implementar el flujo nuevo respetando el flujo viejo (que se mantiene para PlatformAdmin), sin crear microservicios innecesarios, sin romper compatibilidad con los 5 consumidores actuales del `TenantCreatedIntegrationEvent`, y con evidencia arquitectónica (DDD/EDA/SOLID/Clean Architecture) en cada decisión.

**Regla dura del negocio** (Anexo del PDF): una vez `PaymentCompleted`, **el cliente pagó**. Cualquier falla posterior es responsabilidad del sistema. Retry, resume, ManualReview, refund — nunca "pay again".

---

## 2. Bibliografía y patrones aplicados

Este plan aplica patrones publicados de las siguientes fuentes. Cada decisión de fondo cita la fuente.

- **Evans, Eric — "Domain-Driven Design" (Blue Book)**. Capítulos 4 (Bounded Context), 5 (Aggregates), 14 (Context Maps).
- **Vernon, Vaughn — "Implementing Domain-Driven Design" (Red Book)**. Capítulos 2 (Domains/Subdomains/Bounded Contexts) y 8 (Domain Events).
- **Newman, Sam — "Building Microservices" 2ª ed.** Capítulos 3 (When Not to Use Microservices) y 6 (Workflow / Sagas).
- **Richardson, Chris — "Microservices Patterns"**. Pattern 4.2 (Saga), 6.1 (Domain Event), 3.3 (API Gateway).
- **Fowler, Martin — Patterns of Enterprise Application Architecture** y artículos sobre modular monolith (2019+).
- **Kleppmann, Martin — "Designing Data-Intensive Applications"**. Cap. 8 (Distributed Systems Trouble), Cap. 11 (Stream Processing).
- **Stripe engineering blog** — patterns de webhook idempotency, checkout session lifecycle.
- **Auth0, Chargebee, Shopify, Stripe Billing, AWS Marketplace SaaS Onboarding whitepapers** — patrones industriales de tenant provisioning post-pago.

**Regla operativa aplicada** (Newman cap. 3): un bounded context nuevo se hospeda como **módulo dentro de un servicio existente** cuando su vocabulario tiene coincidencia semántica con el vocabulario del host. Extraer a servicio nuevo requiere evidencia dura (escala independiente, propiedad de equipo distinta, cadencia de deploy diferente, aislamiento de fallo crítico). El bounded context "Tenant Onboarding" NO tiene esa evidencia → vive como módulo dentro de Auth.

---

## 3. Decisiones arquitectónicas fundacionales

### 3.1 Bounded context "Tenant Onboarding" vive en Auth como módulo

**NO se crea microservicio nuevo.** Justificación por afinidad de vocabulario (5 de 6 agregados nuevos tienen afinidad natural con Auth):

| Agregado nuevo | Afinidad con Auth |
|---|---|
| `TenantOnboarding` (el proceso) | Media-alta (Auth ya tiene `TenantCreatedConsumer` como proto-orquestador) |
| `EmailVerificationChallenge` (OTP signup) | Alta (Auth ya tiene 3 tipos de challenge: MFA email, MFA sms, phone verification) |
| `TermsVersion` (contenido inmutable) | Alta (Auth ya tiene `TenantTermsAcceptance`) |
| `SubdomainReservation` (pre-tenant) | Alta (Auth ya tiene `TenantSubdomainReservation`) |
| `RegistrationToken` (opaco) | Alta (Auth ya tiene `Invitation`, `PasswordResetToken`, `EmailVerificationToken`) |
| Saga (Wolverine Process Manager) | Media (Wolverine ya está en Auth) |

Solo 2 conceptos NO son afines a Auth: `Checkout` (vive en PaymentApp) y `Receipt PDF` (vive en Documents). Ambos son colaboradores externos, no parte del bounded context Onboarding.

**Estructura de directorios en Auth**:

```
src/Services/Auth/
├── TaxVision.Auth.Domain/
│   ├── Onboarding/                             ← NUEVO módulo
│   │   ├── TenantOnboardings/
│   │   │   ├── TenantOnboarding.cs             (aggregate)
│   │   │   ├── TenantOnboardingStatus.cs       (enum 12 valores)
│   │   │   ├── TenantProvisioningStep.cs       (enum 8 valores)
│   │   │   ├── FailureCode.cs                  (enum classifier)
│   │   │   ├── Events/                         (domain events)
│   │   │   └── OnboardingErrors.cs
│   │   ├── EmailVerification/
│   │   │   └── EmailVerificationChallenge.cs
│   │   ├── TermsVersions/
│   │   │   └── TermsVersion.cs
│   │   ├── SubdomainReservations/
│   │   │   └── OnboardingSubdomainReservation.cs
│   │   └── ValueObjects/
│   │       ├── RegistrationToken.cs
│   │       └── OtpCode.cs
│   └── Terms/                                  ← módulo existente (retrofit)
│       └── TenantTermsAcceptance.cs            (modificado: +TermsVersionId +ContentHash)
├── TaxVision.Auth.Application/
│   ├── Onboarding/                             ← NUEVO módulo
│   │   ├── TenantOnboardings/Commands/
│   │   ├── TenantOnboardings/Queries/
│   │   ├── EmailVerification/Commands/
│   │   ├── TermsVersions/Commands/
│   │   ├── TermsVersions/Queries/
│   │   ├── SubdomainReservations/
│   │   ├── Abstractions/                       (puertos: IOtpCodeGenerator, IOnboardingOtpThrottler, IReceiptDocumentClient, ISecureTokenService, ITokenReferenceStore)
│   │   ├── Consumers/                          (OnboardingPaymentSucceededConsumer)
│   │   ├── Sagas/                              (TenantOnboardingProcessManager)
│   │   └── IntegrationEvents/
├── TaxVision.Auth.Infrastructure/
│   ├── Onboarding/                             ← NUEVO módulo
│   │   ├── Persistence/
│   │   │   ├── Configurations/                 (EF configs)
│   │   │   └── Repositories/
│   │   ├── Security/                           (NumericOtpCodeGenerator, SecureTokenService)
│   │   ├── RateLimit/                          (RedisOnboardingOtpThrottler)
│   │   ├── TokenReferenceStore/                (RedisTokenReferenceStore)
│   │   └── HttpClients/                        (ReceiptDocumentClient, PaymentAppOnboardingClient, TenantProvisioningClient, SubscriptionActivationClient)
└── TaxVision.Auth.Api/
    └── Controllers/
        ├── OnboardingChallengesController.cs   (POST /onboarding/email-challenges/*)
        ├── OnboardingCheckoutController.cs     (POST /onboarding + POST /onboarding/checkout)
        ├── OnboardingRegistrationController.cs (POST /onboarding/register/*)
        ├── OnboardingStatusController.cs       (GET /onboarding/status)
        ├── OnboardingAdminController.cs        (endpoints admin PlatformAdmin)
        ├── InternalOnboardingTokensController.cs (M2M /auth/internal/onboarding/tokens/*)
        └── TermsVersionsController.cs          (GET /auth/onboarding/terms/current, POST /auth/onboarding/terms/publish)
```

**Fronteras**: NetArchTest garantiza que:
- `Auth.Domain/Onboarding/*` no referencia otros módulos de `Auth.Domain/*` salvo por interfaces expuestas o VOs compartidos (`Email`, `SubdomainSlug`).
- `Auth.Application/Onboarding/*` no llama repositorios de otros módulos directamente (usa eventos internos o queries publicadas).
- Ningún archivo fuera de `Onboarding/` referencia agregados internos de `Onboarding/`.

### 3.2 Recibo del pago de onboarding vive en Documents (NO en Billing)

**Nuevo `DocumentType="OnboardingReceipt"` en Documents con template embebido `onboarding.receipt.v1`.** Billing queda intacto (Billing hoy es puro tenant→cliente-final; meter platform→tenant rompe su ontología, ver auditoría en Anexo D).

Documents ya está diseñado como genérico (`DocumentType` es `string`, `Renderers.cs` es `PlaywrightHtmlToPdfConverter` genérico). Agregar el segundo tipo es exactamente para lo que fue diseñado.

**`IssuerProfile` de la plataforma** (dato del emisor: TaxVision como plataforma, no el tenant): se hardcodea en config `Documents:PlatformIssuer:*` en `appsettings.json` para MVP. Se evalúa promover a agregado si crece a >3 casos.

### 3.3 PaymentApp = dueño absoluto del pago, sigue sin orquestar

PaymentApp gana un valor de enum (`SaaSPaymentType.OnboardingInitial`), permite `TenantId=Guid.Empty` **solo** cuando `Type=OnboardingInitial`, y publica 2 eventos nuevos (`OnboardingPaymentSucceeded/Failed`). Reusa el webhook, la idempotencia de `WebhookEvents`, el `StripePaymentAdapter`, la infraestructura completa existente. NO se crea agregado `OnboardingCheckout` separado; se extiende `SaaSPayment`.

### 3.4 Otros servicios: cambios mínimos

- **Tenant**: 1 endpoint M2M nuevo (`POST /tenants/internal/from-onboarding`) + migración `AddOnboardingIdToTenants`. Path viejo `POST /tenants` intacto.
- **Subscription**: 1 endpoint M2M nuevo (`POST /subscriptions/internal/activate-from-onboarding`) + branch condicional en `TenantCreatedConsumer` (si `OnboardingId != null` → early return, no crear trial) + migración `AddOnboardingIdToTenantSubscriptions`.
- **Documents**: 1 handler nuevo + 1 template embebido + 1 endpoint M2M nuevo.
- **Scribe**: 2 template seeds nuevos.
- **Notification**: 2 consumers nuevos.
- **Postmaster**: **cero cambios de código** (excepto rate limit opcional por destinatario en Fase 17).
- **CloudStorage**: cero cambios (Documents ya usa el patrón `SaveFileRequestedIntegrationEvent`).
- **Billing**: **cero cambios**.

### 3.5 Password nunca por RabbitMQ

El password del TenantAdmin viaja de Onboarding a Auth vía HTTP M2M síncrono con TLS + `ServiceOnly` policy + `[AllowActorTypes(Service)]`. Auth hashea inmediatamente con `IPasswordHasher` en la **primera línea** del handler, borra la referencia local, y publica el evento resultado sin password. Auditoría de logs verifica que la variable no aparece en Serilog.

### 3.6 RegistrationToken nunca por RabbitMQ

El `RegistrationToken` raw:
1. Se genera en `Auth.Onboarding` (SecureTokenService, 32 bytes CSRNG + SHA256).
2. Se guarda `SHA256(token)` en `TenantOnboarding.RegistrationTokenHash`.
3. Se guarda el raw temporalmente en Redis con TTL 30s vinculado a un `TokenReference` (Guid).
4. Se publica `OnboardingRegistrationReadyIntegrationEvent` con **solo el `TokenReference`**, sin el raw.
5. El consumer de Notification recibe el evento, hace M2M `GET /auth/internal/onboarding/tokens/{reference}/raw` (ServiceOnly, one-shot: al leer se borra de Redis), y usa el raw solo en memoria para el render Scribe.
6. Scribe embebe el raw en el HTML del email (`href="…/register?token={{registrationToken}}"`).
7. Postmaster envía el email al usuario. El raw termina en el buzón del usuario y en ningún otro lado.

### 3.7 Cero refund automático

Refund solo se ejecuta por acción humana explícita en `POST /auth/onboarding/admin/{id}/cancel-and-refund` con `Reason` obligatorio + `Confirmation="I understand this is irreversible"` en el body. Razones: legal/contable (facturas ya emitidas), fraude (atacante disparando fallas artificiales), costo real (fee de Stripe generalmente no se devuelve), recuperabilidad (99% de fallas post-pago son recuperables con retry o intervención de soporte).

Fallas transitorias → retry automático (Polly, hasta 24h con backoff amplio).
Fallas permanentes → `ProvisioningFailed` + `ManualReview` inmediato.
ManualReview agotado → soporte decide: reanudar, corregir data + reanudar, o cancelar + refund.

---

## 4. Coexistencia de los 3 tokens

Los 3 tokens siguen vivos, cubren 3 casos de uso distintos, **NO se pisan**.

| Token | Path | Emisor | Consumidor | Vida en flujo nuevo |
|---|---|---|---|---|
| **`TenantRegistrationTicket`** (JWT capability, claims `reg_slug + reg_email + purpose=tenant-registration`, TTL 15min) | **Path A: PlatformAdmin crea tenant sin pago** | `POST /auth/subdomains/reserve` (`ReserveSubdomainHandler` en `Auth.Application/TenantDomains/Commands/ReserveSubdomain.cs:78`) | `POST /tenants` (`TenantController.cs:44` con `[AuthorizedByCapabilityToken]` + policy `TenantRegistration`) | **Conservado intacto**. Se usa solo cuando un PlatformAdmin crea tenants operativos sin pasar por checkout. |
| **`Invitation.RawToken`** (opaco, hasheado en `Invitation` aggregate, TTL 7 días) | **Path B: TenantAdmin invita a TenantEmployee, o Auth crea Invitation del TenantAdmin (Path A únicamente)** | `POST /auth/invitations` (`CreateInvitationHandler` en `Auth.Application/Invitations/Commands/CreateInvitation.cs:176`) o `TenantCreatedConsumer.cs:82` (solo si `OnboardingId==null`) | `POST /auth/invitations/accept` (`AcceptInvitationHandler.cs:19`) | **Conservado intacto** para invitaciones internas post-tenant. En path nuevo NO se crea Invitation para el TenantAdmin porque nace vía Saga M2M síncrono. |
| **`RegistrationToken`** (NUEVO, opaco 32 bytes CSRNG, `SHA256` en `TenantOnboarding.RegistrationTokenHash`, TTL 72h, one-shot con `RegistrationTokenUsedAt`) | **Path C: signup con pago (flujo nuevo)** | `TenantOnboarding.SetRegistrationToken` en el consumer `OnboardingPaymentSucceededConsumer` de `Auth.Application/Onboarding/Consumers/` | `POST /onboarding/register/complete` (con validación de hash + guard estado + guard `RegistrationTokenUsedAt IS NULL`) | Nuevo. Vive en el módulo Auth.Onboarding. Se consume AL FINAL (paso 34 del PDF), no al empezar Provisioning — permite reintentar el registro si algo del provisioning falla. |

### Cambios que impacto en los tokens viejos

- **`TenantRegistrationTicket`**: **cero cambios de contrato**. `IJwtTokenGenerator.GenerateTenantRegistrationTicket` (`JwtTokenGenerator.cs:113`) queda igual. `EffectiveTenantRegistrationResolver` (`Tenant.Api/Common/EffectiveTenantRegistrationResolver.cs:17`) queda igual.
- **`Invitation`**: **cero cambios de agregado**. Solo un branch condicional en `Auth.Application/Tenants/IntegrationEvents/TenantCreatedConsumer.cs:56-99`: si `evt.OnboardingId != null` → NO crear Invitation (early return del bloque de Invitation creation). Todo lo demás del consumer (crear `TenantDomain`, `EnsureSystemRolesAsync`, crear `TenantMfaPolicy` default) se conserva.
- **`TenantSubdomainReservation`** (`Auth.Domain/TenantDomains/TenantSubdomainReservation.cs:13`): **cero cambios de contrato**. Sigue viva para Path A. El path nuevo introduce su propia `OnboardingSubdomainReservation` en el módulo Auth.Onboarding (con TTL 60min y FK opcional a `OnboardingId`).
- **`AdminInvitationRawToken` viajando por RabbitMQ** (anti-patrón preexistente en `Auth.Application/Tenants/IntegrationEvents/TenantCreatedConsumer.cs:79-97`): **no se toca en este plan del PayFlow**. Se agrega a la Fase 18 como parte del hardening de credenciales.

---

## 5. Bounded context y responsabilidades por servicio

| Servicio | Bounded context | Rol en flujo nuevo | Alcance de cambios |
|---|---|---|---|
| **Auth** | Identity + Access + **Onboarding** (módulo nuevo) | Host de la Saga, dueño de `TenantOnboarding`, `EmailVerificationChallenge`, `TermsVersion`, `OnboardingSubdomainReservation`. Consume `OnboardingPaymentSucceeded` de PaymentApp. Llama M2M a PaymentApp (checkout), Documents (recibo), Tenant (crear tenant), Subscription (activar), CloudStorage (provisioning). | Alto |
| **PaymentApp** | Payment gateway integration | Nuevo `SaaSPaymentType.OnboardingInitial`; endpoint M2M `POST /payments-app/internal/onboarding/checkout`; branch webhook; 2 eventos nuevos. Reusa infra completa (idempotencia webhook, `SaaSPayment`, `StripePaymentAdapter`). | Medio |
| **Documents** | Document generation pipeline | Nuevo `DocumentType="OnboardingReceipt"`; nuevo handler `GenerateOnboardingReceiptDocumentHandler`; nuevo template embebido `onboarding.receipt.v1`; nuevo endpoint M2M `POST /internal/document-generations/onboarding-receipts`; `PlatformIssuer` config. | Medio |
| **Tenant** | Tenant resource + branding | Nuevo endpoint M2M `POST /tenants/internal/from-onboarding` (ServiceOnly, idempotente por `OnboardingId`); migración `AddOnboardingIdToTenants` con IX filtrado. Path viejo `POST /tenants` intacto. | Bajo |
| **Subscription** | Subscription lifecycle | Nuevo endpoint M2M `POST /subscriptions/internal/activate-from-onboarding` (ServiceOnly, arranca `Active` no `Trialing`); branch en `TenantCreatedConsumer` (`OnboardingId!=null` → early return). Migración `AddOnboardingIdToTenantSubscriptions`. | Bajo |
| **Scribe** | Email template rendering | 2 seeds nuevos en `NotificationTemplateSeedSource.All`: `onboarding.otp_code`, `onboarding.registration_ready` (email HTML con link al recibo PDF + link a completar registro). NO renderiza el recibo PDF — eso es de Documents. | Bajo |
| **Notification** | Notification orchestration | 2 consumers nuevos: `OnboardingOtpRequestedConsumer`, `OnboardingRegistrationReadyConsumer`. Ambos hacen Scribe render → gateway → Postmaster. | Bajo |
| **Postmaster** | Email delivery | **Cero cambios** en MVP. Opcional Fase 17: rate limit per-recipient. | Nulo (o mínimo) |
| **CloudStorage** | Object storage + scan | Cero cambios (Documents ya usa `SaveFileRequestedIntegrationEvent`, patrón Fase D0 existente). | Nulo |
| **Billing** | Tenant→customer invoicing | **Cero cambios**. Billing sigue siendo puro tenant→cliente-final. | Nulo |
| **PaymentClient** | Tenant→customer payment gateway | Cero cambios. | Nulo |

---

## 6. Máquina de estados (12 estados + 8 pasos)

### 6.1 Estados persistidos de `TenantOnboarding.Status`

| Estado | Significado | Transiciones válidas hacia |
|---|---|---|
| `PendingPayment` | Onboarding persistido en UoW #1, aún no hay pago. | `PaymentProcessing`, `Cancelled`, `Expired` |
| `PaymentProcessing` | Checkout creado en Stripe. El usuario está en la sesión de pago. | `PaymentCompleted`, `PaymentFailed`, `Cancelled`, `Expired` |
| `PaymentCompleted` | Webhook `payment_succeeded` procesado (UoW #2). Cliente pagó. | `RegistrationPending` |
| `RegistrationPending` | `RegistrationToken` generado y persistido (UoW #3). Email en outbox. | `Provisioning`, `Expired` (tras 72h sin uso), `Refunded` (manual) |
| `Provisioning` | Formulario final validado. Saga arrancada (UoW #4). | `Completed`, `ProvisioningFailed`, `ManualReview` |
| `ProvisioningFailed` | Un paso de la Saga falló. Estado detenido con `FailedStep + FailureCode + FailureReason`. | `Provisioning` (retry), `ManualReview` (retry agotado), `Refunded` (manual) |
| `ManualReview` | Requiere intervención humana. | `Provisioning` (resume), `Refunded` (cancel), `Completed` (force-complete excepcional) |
| `Completed` | Todos los pasos obligatorios finalizaron. Token consumido. Tenant operativo. | (final) |
| `PaymentFailed` | Proveedor de pago rechazó el cobro. | `Cancelled` (manual) |
| `Cancelled` | Onboarding cancelado antes de completar. | (final) |
| `Expired` | TTL agotado (nunca pagó, o token no usado en 72h). | (final) |
| `Refunded` | Refund emitido a Stripe + compensaciones ejecutadas. | (final) |

**Guards en el aggregate**: cada método de transición (`MarkPaymentCompleted`, `SetRegistrationToken`, `StartProvisioning`, etc.) valida `Status IN (estados permitidos)` y retorna `Result.Failure(OnboardingErrors.InvalidState)` en caso contrario. Idempotencia: llamar `MarkPaymentCompleted` dos veces con el mismo `PaymentReference` es no-op exitoso.

### 6.2 Paso actual de provisioning (`TenantOnboarding.CurrentStep`)

Sub-estado durante `Status=Provisioning` o `Status=ProvisioningFailed`.

| Step | Cuándo se setea |
|---|---|
| `None` | Antes de arrancar Saga |
| `Tenant` | Comando `CreateTenantForOnboarding` en vuelo |
| `TenantAdmin` | Tras `TenantCreatedForOnboarding`, comando `CreateTenantOwner` en vuelo |
| `Subscription` | Tras `TenantOwnerCreated`, comando `ActivateSubscription` en vuelo |
| `CloudStorage` | Tras `SubscriptionActivated`, comando `ProvisionStorage` en vuelo |
| `Subdomain` | Tras `StorageProvisioned`, comando `ActivateSubdomain` en vuelo |
| `Defaults` | Tras `SubdomainActivated`, comando `ConfigureDefaults` en vuelo |
| `Completed` | Tras `DefaultsConfigured`, UoW #8 final marcó `Status=Completed` |

`FailedStep` se setea cuando `Status=ProvisioningFailed` y guarda el step exacto donde ocurrió el fallo.

### 6.3 8 Units of Work locales

Cada UoW es una transacción SQL local en UN servicio, con `BEGIN TRANSACTION / COMMIT / ROLLBACK`. La Saga coordina las 8 UoW, pero **NO hay transacción distribuida**.

| UoW | Servicio | Responsabilidad | Resultado |
|---|---|---|---|
| #1 | Auth | Crear `TenantOnboarding` en `PendingPayment` | Persist buyer + PlanId, ANTES de crear checkout |
| #2 | PaymentApp | Confirmar pago del webhook | Inbox `WebhookEvents` + `SaaSPayment.Status=Succeeded` + Outbox `OnboardingPaymentSucceeded` |
| #3 | Auth | Preparar registro | `RegistrationTokenHash + ExpiresAt` + `Status=RegistrationPending` + Outbox `OnboardingRegistrationReady` |
| #4 | Auth | Iniciar Saga | `Status=Provisioning` + `OfficeName + RequestedSubdomain + TermsVersionId + ContentHash + IP + UA` |
| #5 | Tenant | Crear Tenant | Persist Tenant idempotente por `OnboardingId` + Outbox `TenantCreatedForOnboarding` |
| #6 | Auth | Crear TenantAdmin User | Persist User (password hasheado) + Outbox `TenantOwnerCreated` |
| #7 | Subscription | Activar suscripción | Persist `TenantSubscription` en `Active` + Outbox `SubscriptionActivatedForOnboarding` |
| #8 | Auth | Finalizar Onboarding | `TenantId + UserId + SubscriptionId + RegistrationTokenUsedAt=NOW + Status=Completed` + Outbox `TenantOnboardingCompleted` |

Pasos intermedios de la Saga (CloudStorage, Subdomain, Defaults) usan patrón similar: cada uno en su servicio, con su propia UoW local + Outbox del evento resultado.

---

## 7. Modelo de fallas y compensaciones

### 7.1 Clasificación de errores (`FailureClassifier`)

| Código | Tipo | Retry |
|---|---|---|
| `Tenant.DbUnavailable`, `Subscription.DbUnavailable`, `CloudStorage.MinioTimeout`, `Cloudflare.Timeout` | Transient | Sí, Polly 3 intentos con backoff 1s/5s/30s. Si agotado → sigue reintentando en background con backoff amplio (5min/15min/1h) hasta 24h. Alerta a ops. |
| `Rabbit.Unavailable` | Transient | Wolverine local |
| `Http.5xx` M2M | Transient | Sí |
| `Http.4xx` M2M salvo 409 | Permanent | ManualReview inmediato |
| `Tenant.SubdomainConflict` | Permanent | ManualReview (cambiar subdomain) |
| `User.EmailConflict` | Permanent | ManualReview (cambiar email) |
| `Plan.NotFound`, `Plan.Deactivated` | Permanent | ManualReview (cambiar plan) |
| `Terms.VersionInactive` | Permanent | ManualReview (aceptar nueva versión) |
| `Payment.Refunded` (edge: Stripe emitió refund antes de completar provisioning) | Permanent | Auto-cancel del onboarding |
| `Config.PlatformIssuerMissing` | Permanent | ManualReview + fix ops |
| `Onboarding.InvalidState` (bug lógico) | Permanent | ManualReview + investigación |

### 7.2 Compensaciones manuales (tras ManualReview)

Endpoints admin en `OnboardingAdminController` (PlatformAdmin), todos con `Idempotency-Key` obligatorio:

| Acción | Comportamiento |
|---|---|
| `POST /auth/onboarding/admin/{id}/resume` | Reanuda Saga desde `CurrentStep`. Todos los pasos idempotentes por `OnboardingId`. |
| `POST /auth/onboarding/admin/{id}/update-and-resume` | Permite corregir `RequestedSubdomain` / `PlanId` y reanudar. |
| `POST /auth/onboarding/admin/{id}/cancel-and-refund` | Requiere `Reason` + `Confirmation="I understand this is irreversible"`. Publica `OnboardingRefundRequested` → PaymentApp llama `Stripe.Refunds.CreateAsync` con `IdempotencyKey`. Publica `OnboardingCancelRequested` → Subscription cancela, Auth deactiva user, Tenant marca `Closed`. Marca `Status=Refunded`. |
| `POST /auth/onboarding/admin/{id}/force-complete` | Escape hatch excepcional: marca `Completed` a pesar de un paso fallido. Debe ser raro; queda auditado. |

### 7.3 Compensaciones = acciones inversas semánticas, no DELETE

- `Subscription.CancelImmediately(reason)` — no elimina, marca cancelled.
- `User.Deactivate(reason)` — no elimina, marca inactive. Sessions se invalidan por `SessionDenylist`.
- `Tenant.MarkClosed(reason)` — no elimina, marca closed. Downstream reacciona bloqueando acceso.

Data queda para auditoría. Reversibles bajo acción manual futura si soporte lo decide.

---

## 8. Formato de prompts de implementación

Cada fase incluye un prompt exacto para Sonnet 5. Formato estándar:

```
## PROMPT DE IMPLEMENTACIÓN — PayFlow — Fase X

**Contexto**: [1-2 frases explicando dónde vive esta fase en el flujo global]

**Referencia obligatoria**: leer PRIMERO `documents/architecture/payflowguide/PayFlow_Implementation_Plan.md`
secciones [X.Y, X.Z]. NO tocar código antes de leer.

**Objetivo**: [descripción precisa de lo que debe quedar funcionando al final]

**Archivos a inspeccionar antes de tocar nada**:
- [paths con líneas específicas si aplica]

**Archivos a crear**:
- [path relativo con propósito]

**Archivos a modificar**:
- [path relativo con qué exactamente cambiar]

**Servicios afectados**: [lista + qué cambia en cada uno]

**Eventos publicados / consumidos** (nombres exactos con namespace completo)

**Commands / Handlers** (nombres exactos)

**Endpoints** (verbo + ruta + auth + permission)

**Tablas + migración EF** (nombre migración + descripción)

**Cambios permitidos** en otros servicios: [explícito o "ninguno"]

**Cambios prohibidos**: [lista clara]

**Riesgos**: [ordenados por severidad]

**Pruebas unitarias**: [qué debe cubrir]

**Pruebas de integración**: [qué debe cubrir con Testcontainers si aplica]

**Verificación manual** (comandos):
- `dotnet build TaxVision.slnx`
- `dotnet test TaxVision.slnx` (verde antes y después)
- `dotnet ef database update --project X --startup-project Y`
- [curl/Postman opcionales]

**Criterios de aceptación**:
1. [criterio observable]
2. [criterio observable]
...

**Al finalizar, reportar en español**:
- Qué se implementó (bullet points)
- Qué no se pudo hacer y por qué (si aplica)
- Warnings/errores encontrados
- Estado de tests (# passing / # total por proyecto tocado)
```

---

# Parte II — Fases de implementación

## Fase 0 — Baseline snapshot

**Objetivo**: capturar estado exacto antes de tocar cualquier cosa. Reversibilidad total. Cero riesgo.

**Salida**: `documents/architecture/payflowguide/00_Baseline.md` con:
- `dotnet build TaxVision.slnx` output resumido
- `dotnet test TaxVision.slnx` counts por proyecto (esperado: monorepo verde)
- Grep de endpoints en Auth/PaymentApp/Tenant/Subscription/Documents/Billing/Notification/Scribe/Postmaster (`Controllers/*.cs`)
- Grep de integration events en `BuildingBlocks/Messaging/`
- Grep de aggregates en `Domain/`
- Grep de "Saga|ProcessManager|Orchestrat|Onboarding" en todo el monorepo (esperado: cero matches funcionales)
- Fecha + hash de commit HEAD

**Cambios permitidos**: solo el archivo `.md` nuevo. **Cambios prohibidos**: cualquier archivo productivo.

**Criterios**: build+test verde, `.md` con las 6 secciones.

**Prompt**:

```
## PROMPT DE IMPLEMENTACIÓN — PayFlow — Fase 0

**Contexto**: primer paso del plan PayFlow. Capturar estado exacto antes de
tocar nada. Reversible.

**Referencia obligatoria**: leer PRIMERO
`documents/architecture/payflowguide/PayFlow_Implementation_Plan.md`
sección "Fase 0". NO tocar código productivo.

**Objetivo**: generar
`documents/architecture/payflowguide/00_Baseline.md` con snapshot
completo del monorepo antes del PayFlow.

**Contenido del .md**:
1. Header: fecha + `git rev-parse HEAD`.
2. Resultado de `dotnet build TaxVision.slnx` (resumen).
3. Resultado de `dotnet test TaxVision.slnx` (# tests passing / total por
   proyecto).
4. Grep de endpoints en:
   `src/Services/{Auth,PaymentApp,Tenant,Subscription,Documents,Billing,
   Notification,Scribe,Postmaster}/**/Controllers/*.cs` — listar
   ruta+verbo+controller.
5. Grep de integration events en
   `src/BuildingBlocks/Messaging/**/*IntegrationEvent*.cs`.
6. Grep de aggregates: `src/Services/*/TaxVision.*.Domain/**/*.cs` —
   listar clases que heredan `AggregateRoot`.
7. Grep de "Saga|ProcessManager|Orchestrat|OnboardingProcess|SignupIntent
   |PayflowOrchestrator" en `src/` — esperado: cero matches funcionales
   (ignorar Stripe Connect OnboardingStep y RawMessageSyncOrchestrator
   documentados en el plan).

**Cambios permitidos**: solo el .md nuevo.

**Cambios prohibidos**: cualquier archivo productivo del monorepo.

**Criterios**: build+test verde antes y después. .md con las 7 secciones.

**Al finalizar, reportar en español**: counts globales de tests, cantidad
de endpoints por servicio, cantidad de events, hash del commit.
```

---

## Fase 1 — Extracción formal del flujo

**Objetivo**: formalizar los 40 pasos del PDF + PNG en `.md` estructurado que servirá de referencia para todas las fases.

**Salida**: `documents/architecture/payflowguide/01_FlowSpec.md` con:
- Tabla de 40 pasos: `{#, nombre, actor, servicio dueño}`
- Tabla de 12 estados
- Tabla de 8 pasos de provisioning
- Tabla de 8 UoW
- Matriz de fallas (Anexo C del PDF)
- Huecos no especificados marcados literalmente "Flujo nuevo no especifica este punto — recomendación: X"

**Cambios permitidos**: solo el .md nuevo.

**Prompt**:

```
## PROMPT DE IMPLEMENTACIÓN — PayFlow — Fase 1

**Contexto**: formalizar el flujo del PDF+PNG en un .md estructurado.

**Referencia obligatoria**: leer PRIMERO
`documents/architecture/payflowguide/PayFlow_Implementation_Plan.md`
sección "Fase 1". Leer también
`Implementaciones/PayFlowNew/Tenant_Onboarding_Flujo_Seguro_Arquitectura.pdf`
completo y `Implementaciones/PayFlowNew/flowpay.png`.

**Objetivo**: generar
`documents/architecture/payflowguide/01_FlowSpec.md` con:
a. Tabla de los 40 pasos {#, nombre PDF, actor humano/sistema, servicio
   dueño según sección 5 del Plan}.
b. Tabla de los 12 estados `TenantOnboardingStatus`.
c. Tabla de los 8 pasos `TenantProvisioningStep`.
d. Tabla de las 8 UoW.
e. Matriz de fallas del Anexo C del PDF, con columna extra
   "clasificación (transient/permanent)".
f. Huecos no especificados marcados textualmente "Flujo nuevo no
   especifica este punto — recomendación: [X]".

**Cambios permitidos**: solo el .md nuevo.

**Criterios**: .md con las 6 secciones. Cada paso mapeado a un servicio.
```

---

## Fase 2 — Matriz comparativa actual vs nuevo

**Objetivo**: expandir la matriz de la sección "Parte 5" del análisis previo en un `.md` con verificación de cada `archivo:línea` citado.

**Salida**: `documents/architecture/payflowguide/02_ComparisonMatrix.md` con la matriz completa (columnas: paso nuevo, existe hoy, servicio hoy, servicio recomendado, eventos actuales, eventos necesarios, tablas nuevas, impacto, riesgo, decisión, fase).

**Cambios permitidos**: solo el .md nuevo.

**Prompt**:

```
## PROMPT DE IMPLEMENTACIÓN — PayFlow — Fase 2

**Contexto**: matriz de referencia para las fases 3-19.

**Referencia obligatoria**: leer PRIMERO
`documents/architecture/payflowguide/PayFlow_Implementation_Plan.md` completo
(especialmente sección 5 y las fases 3-19). Leer también
`documents/architecture/payflowguide/00_Baseline.md` y
`documents/architecture/payflowguide/01_FlowSpec.md`.

**Objetivo**: generar
`documents/architecture/payflowguide/02_ComparisonMatrix.md` con matriz
completa 40 filas (una por paso del PDF), 11 columnas:
{Paso#, Existe hoy Sí/No/Parcial, Servicio hoy, Servicio recomendado,
Eventos actuales, Eventos necesarios, Tablas nuevas/cambios,
Impacto (Bajo/Medio/Alto), Riesgo, Decisión, Fase donde se implementa}.

Verificar cada archivo:línea citado abriendo el archivo. Si un path cambió
desde la auditoría original, actualizarlo. Marcar celdas "PENDING VERIFY"
si no se puede confirmar el path hoy.

**Cambios permitidos**: solo el .md nuevo.

**Criterios**: 40 filas, cero celdas vacías, cada archivo:línea verificado.
```

---

## Fase 3 — Auth: scaffolding del módulo Onboarding

**Objetivo**: crear la estructura de directorios del módulo Onboarding dentro de Auth sin lógica todavía. Establecer NetArchTest de fronteras.

**Archivos a crear**:
- `src/Services/Auth/TaxVision.Auth.Domain/Onboarding/.gitkeep` (o placeholder)
- Subcarpetas vacías: `Onboarding/TenantOnboardings/`, `Onboarding/EmailVerification/`, `Onboarding/TermsVersions/`, `Onboarding/SubdomainReservations/`, `Onboarding/ValueObjects/`.
- Idem para `Auth.Application/Onboarding/`, `Auth.Infrastructure/Onboarding/`.
- `deploy/tests/TaxVision.Auth.Tests/Architecture/OnboardingModuleArchitectureTests.cs` con NetArchTest:
  - Reglas: `Auth.Domain/Onboarding/**` no depende de `Auth.Domain/{Users, Sessions, Mfa, Credentials, ...}/*` salvo por VOs compartidos (`Email`, `SubdomainSlug`).
  - `Auth.Application/Onboarding/**` no llama repositorios de otros módulos directamente.
  - Ningún archivo fuera de `Onboarding/` referencia clases internas de `Onboarding/`.

**Cambios prohibidos**: cualquier cosa fuera de Auth. No agregar lógica en las carpetas.

**Verificación**: `dotnet build` + `dotnet test` verde. NetArchTest ejecuta y pasa (con módulo vacío).

**Prompt**:

```
## PROMPT DE IMPLEMENTACIÓN — PayFlow — Fase 3

**Contexto**: preparar el módulo `Onboarding` dentro de Auth como bounded
context modular. Solo scaffolding, sin lógica.

**Referencia obligatoria**: leer PRIMERO
`documents/architecture/payflowguide/PayFlow_Implementation_Plan.md`
secciones 3.1, 5, 8, Fase 3. NO tocar código fuera de Auth.

**Objetivo**: crear estructura de directorios del módulo Onboarding en las
3 capas de Auth + tests de arquitectura con NetArchTest.

**Estructura a crear** (subcarpetas vacías con .gitkeep):
- `src/Services/Auth/TaxVision.Auth.Domain/Onboarding/{TenantOnboardings,
  EmailVerification,TermsVersions,SubdomainReservations,ValueObjects}/`
- `src/Services/Auth/TaxVision.Auth.Application/Onboarding/{
  TenantOnboardings/Commands,TenantOnboardings/Queries,
  EmailVerification/Commands,TermsVersions/Commands,TermsVersions/Queries,
  SubdomainReservations,Abstractions,Consumers,Sagas,IntegrationEvents}/`
- `src/Services/Auth/TaxVision.Auth.Infrastructure/Onboarding/{
  Persistence/Configurations,Persistence/Repositories,Security,
  RateLimit,TokenReferenceStore,HttpClients}/`

**Test a crear**:
`deploy/tests/TaxVision.Auth.Tests/Architecture/OnboardingModuleArchitectureTests.cs`
con 3 tests NetArchTest:
1. `Onboarding_Domain_DoesNotDependOnOtherAuthModules`: excepto
   VOs compartidos declarados (`Email`, `SubdomainSlug`).
2. `Onboarding_Application_DoesNotReferenceOtherAuthApplicationRepos`
   directamente.
3. `NonOnboarding_Files_DoNotReferenceOnboardingInternals`.

**Cambios permitidos**: solo lo listado arriba.

**Cambios prohibidos**: cualquier archivo fuera de Auth. NO agregar lógica
funcional. NO tocar los módulos existentes de Auth.

**Verificación**:
- `dotnet build TaxVision.slnx` verde.
- `dotnet test TaxVision.slnx --filter "FullyQualifiedName~Onboarding"`
  verde con 3 tests nuevos.

**Criterios**: estructura creada, NetArchTest ejecuta y pasa (vacío no
viola nada), build verde.

**Al finalizar, reportar en español**: paths creados, tests agregados,
resultado build+test.
```

---

## Fase 4 — Auth: TenantOnboarding aggregate + estados

**Objetivo**: implementar el aggregate `TenantOnboarding` con los 23 campos del paso 6 del PDF + los 12 estados + 8 pasos de provisioning + eventos de dominio + migración EF.

**Archivos a crear**:
- `Auth.Domain/Onboarding/TenantOnboardings/TenantOnboarding.cs` (aggregate)
- `Auth.Domain/Onboarding/TenantOnboardings/TenantOnboardingStatus.cs` (enum 12 valores)
- `Auth.Domain/Onboarding/TenantOnboardings/TenantProvisioningStep.cs` (enum 8 valores)
- `Auth.Domain/Onboarding/TenantOnboardings/FailureCode.cs` (enum)
- `Auth.Domain/Onboarding/TenantOnboardings/OnboardingErrors.cs`
- `Auth.Domain/Onboarding/TenantOnboardings/Events/TenantOnboardingCreated.cs`
- `Auth.Domain/Onboarding/TenantOnboardings/Events/TenantOnboardingPaymentCompleted.cs`
- `Auth.Domain/Onboarding/TenantOnboardings/Events/TenantOnboardingRegistrationReady.cs`
- `Auth.Domain/Onboarding/TenantOnboardings/Events/TenantOnboardingProvisioningStarted.cs`
- `Auth.Domain/Onboarding/TenantOnboardings/Events/TenantOnboardingCompleted.cs`
- `Auth.Domain/Onboarding/TenantOnboardings/Events/TenantOnboardingProvisioningFailed.cs`
- `Auth.Domain/Onboarding/ValueObjects/RegistrationTokenHash.cs` (VO)
- `Auth.Infrastructure/Onboarding/Persistence/Configurations/TenantOnboardingConfiguration.cs`
- Migración EF: `AddOnboardingTenantOnboardings`
- Tests domain: `deploy/tests/TaxVision.Auth.Tests/Onboarding/TenantOnboardingTests.cs`

**Reglas duras del aggregate**:
- No public setters. Constructor privado. Factory `Create(email, planId, firstName, lastName, phone)`.
- Métodos de transición: `MarkPaymentProcessing(paymentId, providerSessionRef)`, `MarkPaymentCompleted(paymentReference, paidAtUtc)`, `SetRegistrationToken(hash, expiresAtUtc)`, `StartProvisioning(officeName, subdomain, termsVersionId, contentHash, ip, ua)`, `SetTenantCreated(tenantId)`, `SetTenantAdminCreated(userId)`, `SetSubscriptionActivated(subscriptionId)`, `MarkStepCompleted(step)`, `MarkStepFailed(step, code, reason)`, `MarkProvisioningFailed(failedStep, code, reason)`, `MarkManualReview(reason)`, `MarkCompleted()`, `ConsumeRegistrationToken()`.
- Guards en cada transición: valida `Status` actual + retorna `Result.Failure(OnboardingErrors.InvalidState)`.
- **Password NUNCA en el aggregate**.
- Idempotencia: `MarkPaymentCompleted` con mismo `PaymentReference` = no-op exitoso.

**Campos exactos del aggregate** (paso 6 del PDF):
```
{ Id, FirstName, LastName, Email, EmailVerifiedAt, Phone, PlanId, Status,
  PaymentId, PaymentStatus, PaymentReference, PaymentCompletedAt,
  RegistrationTokenHash, RegistrationTokenExpiresAt, RegistrationTokenUsedAt,
  OfficeName, RequestedSubdomain,
  TermsVersionId, TermsContentHash, TermsAcceptedAt, AcceptedFromIp, UserAgent,
  TenantId, UserId, SubscriptionId,
  CreatedAt, ProvisioningStartedAt, RegistrationCompletedAt,
  FailedStep, FailureCode, FailureReason, CurrentStep }
```

**EF config**:
- Table `Onboarding.TenantOnboardings`
- PK `Id`
- Unique filtered index `(RegistrationTokenHash) WHERE RegistrationTokenHash IS NOT NULL`
- Index `(Email, Status)` para query de "tiene onboarding pending?"
- Index `(Status, CreatedAt)` para dashboards admin

**Tests**: mínimo 25 tests domain cubriendo happy path + guards de estado inválido + idempotencia.

**Cambios prohibidos**: cualquier archivo fuera de Auth.

**Prompt**: [ver formato estándar, incluir todo lo anterior]

---

## Fase 5 — Auth: EmailVerificationChallenge (OTP)

**Objetivo**: aggregate + commands + handlers + endpoints públicos + rate limiter Redis para verificación de email en el signup.

**Archivos a crear**:
- `Auth.Domain/Onboarding/EmailVerification/EmailVerificationChallenge.cs` (aggregate)
- `Auth.Application/Onboarding/EmailVerification/Commands/CreateEmailChallengeCommand.cs` + Handler
- `Auth.Application/Onboarding/EmailVerification/Commands/VerifyEmailChallengeCommand.cs` + Handler
- `Auth.Application/Onboarding/EmailVerification/Commands/ResendEmailChallengeCommand.cs` + Handler
- `Auth.Application/Onboarding/Abstractions/IOnboardingOtpThrottler.cs`
- `Auth.Application/Onboarding/Abstractions/IOtpCodeGenerator.cs`
- `Auth.Infrastructure/Onboarding/RateLimit/RedisOnboardingOtpThrottler.cs`
- `Auth.Infrastructure/Onboarding/Security/NumericOtpCodeGenerator.cs` (6 dígitos CSRNG)
- `Auth.Api/Controllers/OnboardingChallengesController.cs`
- `Auth.Infrastructure/Onboarding/Persistence/Configurations/EmailVerificationChallengeConfiguration.cs`
- Migración `AddOnboardingEmailVerificationChallenges`
- `BuildingBlocks/Messaging/AuthIntegrationEvents/OnboardingOtpRequestedIntegrationEvent.cs`
- Tests

**Aggregate `EmailVerificationChallenge`**:
```
{ Id, Email, OtpHash, ExpiresAtUtc (10min), Attempts (int),
  ResendCount (int), VerifiedAt?, CreatedAtUtc }
```
- `OtpHash = SHA256(challengeId + ":" + otpCode)` (salted con challengeId)
- Método `Verify(rawCode)`: constant-time compare con `CryptographicOperations.FixedTimeEquals`, incrementa `Attempts` en fallo, marca `VerifiedAt` en éxito. Después de `MaxAttempts=5` bloquea.
- Método `Resend()`: incrementa `ResendCount`, regenera `OtpHash`, resetea `Attempts=0`, publica evento.

**Endpoints**:

| Verbo | Ruta | Body | Auth | Rate limit |
|---|---|---|---|---|
| POST | `/onboarding/email-challenges` | `{email}` | Anonymous | policy `onboarding-otp-create` (30/hora por IP) |
| POST | `/onboarding/email-challenges/{challengeId}/verify` | `{code}` | Anonymous | policy `onboarding-otp-verify` (10/challenge, 60/hora por IP) |
| POST | `/onboarding/email-challenges/{challengeId}/resend` | (vacío) | Anonymous | cooldown 60s por challenge, max 5 resends |

**Rate limiter** (`IOnboardingOtpThrottler` con Redis):
- Max 5 challenges por email/hora
- Max 10 challenges por IP/hora
- Cooldown resend 60s por challenge
- Max ResendCount 5 por challenge
- Fail-closed

**Evento publicado** `OnboardingOtpRequestedIntegrationEvent`:
```
{ ChallengeId, Email, OtpCode (claro, único punto donde el código viaja),
  ExpiresAtUtc, FirstNameHint?, CorrelationId }
```

**Cambios prohibidos**: fuera de Auth.

---

## Fase 6 — Auth: TermsVersion + retrofit TenantTermsAcceptance

**Objetivo**: implementar la Opción C (`TermsVersionId + ContentHash` en `TenantTermsAcceptance` + tabla nueva `TermsVersion` inmutable + migración de datos legacy).

**Archivos a crear** (módulo Onboarding):
- `Auth.Domain/Onboarding/TermsVersions/TermsVersion.cs` (aggregate: `Id, Kind (TermsOfService|PrivacyPolicy), Version, ContentUri, ContentHash, EffectiveFromUtc, EffectiveUntilUtc?, Locale, CreatedAtUtc, CreatedByUserId`)
- `Auth.Domain/Onboarding/TermsVersions/TermsKind.cs`
- `Auth.Application/Onboarding/TermsVersions/Commands/PublishTermsVersionCommand.cs` + Handler (PlatformAdmin)
- `Auth.Application/Onboarding/TermsVersions/Queries/GetCurrentTermsVersionQuery.cs`
- `Auth.Application/Onboarding/TermsVersions/Queries/GetTermsVersionByIdQuery.cs`
- `Auth.Api/Controllers/TermsVersionsController.cs`
- `Auth.Infrastructure/Onboarding/Persistence/Configurations/TermsVersionConfiguration.cs`
- Migración `AddOnboardingTermsVersions`

**Endpoints TermsVersions**:
- `GET /auth/onboarding/terms/current?kind=TermsOfService&locale=en-US` (Anonymous) → `{TermsVersionId, Version, ContentUri, ContentHash, EffectiveFromUtc}`
- `POST /auth/onboarding/terms/publish` (PlatformAdmin) → publica una nueva versión

**Archivos a modificar** (módulo Terms existente en Auth):
- `Auth.Domain/Terms/TenantTermsAcceptance.cs`: agregar `TermsVersionId (Guid, required)`, `ContentHash (string(64), required)`, `AcceptedInContext (nvarchar(32), required: "Onboarding"|"ReAcceptance"|"Update"|"LegacyPreV2")`, renombrar `IpAddress` → `AcceptedFromIp` (mantener columna DB con `HasColumnName("IpAddress")` para no romper migración).
- `Auth.Infrastructure/Configurations/TermsConfigurations.cs`: unique index `(TenantId, AcceptedByUserId, TermsVersionId)`.
- Migración `RetrofitTermsAcceptancesWithVersionId`: (1) crear seed row en `TermsVersions` con `Kind=TermsOfService, Version='legacy-2026-07-14', ContentUri=NULL, ContentHash=NULL, EffectiveFromUtc=<fecha migración>`. (2) agregar columnas nuevas en `TenantTermsAcceptances`. (3) backfill filas existentes: `TermsVersionId=<legacy>, ContentHash=NULL, AcceptedInContext='LegacyPreV2'`. (4) hacer columnas nuevas NOT NULL.

**Nuevo command en `Auth.Application/Terms/Commands/`**:
- `AcceptTermsFromOnboardingCommand {TenantId, UserId, TermsVersionId, ContentHash, AcceptedFromIp, UserAgent}` (ServiceOnly, invocado por Saga en UoW #8).

**Tests**: publicar version, obtener current, aceptar con versión válida, guard "no aceptar versión inactiva", idempotencia por unique index, migración de datos legacy.

**Cambios prohibidos**: PaymentApp, Tenant, Subscription, Documents, Notification.

---

## Fase 7 — Auth: fitness function tests

**Objetivo**: matriz exhaustiva de tests para las transiciones de estado del `TenantOnboarding`. NO cambia código productivo.

**Archivos a crear**:
- `deploy/tests/TaxVision.Auth.Tests/Onboarding/StatusTransitionsMatrixTests.cs`

**Cobertura mínima**:
- 12 estados × ~12 métodos ≈ 140 casos, agrupados en `[Theory]` con `MemberData`.
- Cada caso verifica {éxito | failure con `OnboardingErrors.InvalidState`}.
- Idempotencia: cada método llamado 2 veces = mismo resultado.
- Guards contra transiciones hacia atrás.

**Cambios prohibidos**: código productivo.

---

## Fase 8 — PaymentApp: OnboardingInitial + endpoint checkout

**Objetivo**: extender PaymentApp para aceptar pagos de onboarding sin tenant preexistente, correlacionados por `OnboardingId`.

**Archivos a modificar** (PaymentApp):
- `PaymentApp.Domain/SaaSPayments/SaaSPaymentType.cs`: agregar valor `OnboardingInitial`.
- `PaymentApp.Domain/SaaSPayments/SaaSPayment.cs`: agregar propiedad `OnboardingId? (Guid)`; nueva factory `CreateForOnboarding(onboardingId, planId, amount, currency, idempotencyKey)` que permite `TenantId=Guid.Empty` **solo si `Type=OnboardingInitial`**.
- `PaymentApp.Infrastructure/Persistence/Configurations/SaaSPaymentConfiguration.cs`: agregar mapping de `OnboardingId` + unique filtered index `(OnboardingId) WHERE OnboardingId IS NOT NULL`.
- `PaymentApp.Application/Webhooks/ProcessStripeWebhookHandler.cs:112`: branch — si `event.data.object.metadata["onboardingId"] != null`, publica `OnboardingPaymentSucceededIntegrationEvent` (o `Failed`).

**Archivos a crear**:
- `PaymentApp.Application/OnboardingCheckouts/Commands/CreateOnboardingCheckoutCommand.cs` + Handler (M2M ServiceOnly).
- `PaymentApp.Api/Controllers/InternalOnboardingCheckoutsController.cs`: `POST /payments-app/internal/onboarding/checkout` (`[Authorize(Policy="ServiceOnly")]` + `[AllowActorTypes(Service)]`).
- `BuildingBlocks/Messaging/PaymentAppIntegrationEvents/OnboardingPaymentSucceededIntegrationEvent.cs` `{OnboardingId, PaymentId, PlanId, AmountPaid, Currency, PaidAtUtc, ProviderPaymentReference, PaymentMethodMasked, CorrelationId}`.
- `BuildingBlocks/Messaging/PaymentAppIntegrationEvents/OnboardingPaymentFailedIntegrationEvent.cs`.
- Migración `AddOnboardingIdToSaaSPayments`.

**Endpoint**:
- `POST /payments-app/internal/onboarding/checkout` recibe `{OnboardingId, PlanId, PlanPriceCents, Currency, SuccessUrl, CancelUrl, IdempotencyKey}` → devuelve `{PaymentId, CheckoutUrl, ProviderSessionId, ExpiresAtUtc}`.
- Handler: `IProviderCheckoutClient.CreateCheckoutSessionAsync` con `metadata["onboardingId"]=onboardingId` (patrón compatible con `StripePaymentAdapter`). Persiste `SaaSPayment` con `Type=OnboardingInitial`, `Status=Pending`, `OnboardingId=X`, `IdempotencyKey`.
- Idempotencia: reusa el patrón existente `GetByIdempotencyKeyAsync` (`ChargeSaaSPaymentHandler.cs:35`).

**Nuevo M2M client** en Auth Registry: `payment-app` (si aún no lo tenía, verificar).

**Tests**: crear checkout OK, doble creación con misma IdempotencyKey = mismo response sin duplicar, webhook OnboardingInitial publica evento correcto, webhook duplicado no re-publica.

**Cambios prohibidos**: Auth, Tenant, Subscription, Documents, Notification/Scribe/Postmaster.

---

## Fase 9 — Auth: consumer + generación RegistrationToken

**Objetivo**: cerrar el ciclo pago→email con token. Auth consume `OnboardingPaymentSucceeded`, genera `RegistrationToken`, persiste hash, publica evento con `TokenReference` (sin raw).

**Archivos a crear**:
- `Auth.Application/Onboarding/Consumers/OnboardingPaymentSucceededConsumer.cs`
- `Auth.Application/Onboarding/Abstractions/ISecureTokenService.cs`
- `Auth.Infrastructure/Onboarding/Security/SecureTokenService.cs` (32 bytes CSRNG + SHA256)
- `Auth.Application/Onboarding/Abstractions/ITokenReferenceStore.cs`
- `Auth.Infrastructure/Onboarding/TokenReferenceStore/RedisTokenReferenceStore.cs` (TTL 30s, one-shot: al leer se borra)
- `Auth.Api/Controllers/InternalOnboardingTokensController.cs`: `GET /auth/internal/onboarding/tokens/{reference}/raw` (ServiceOnly, one-shot).
- `Auth.Application/Onboarding/TokenReferences/Queries/ResolveRegistrationTokenReferenceQuery.cs`
- `BuildingBlocks/Messaging/AuthIntegrationEvents/OnboardingRegistrationReadyIntegrationEvent.cs` `{OnboardingId, TokenReference (Guid), Email, FirstName, PlanName, PriceFormatted, PaidAtUtc, RegistrationUrlBase, CorrelationId}` — SIN raw token.

**Flujo del consumer**:
1. Recibe `OnboardingPaymentSucceededIntegrationEvent`.
2. Actualiza `TenantOnboarding.MarkPaymentCompleted(paymentReference, paidAtUtc)`.
3. `SecureTokenService.GenerateOpaqueToken(32bytes)` → `raw + hash`.
4. `TenantOnboarding.SetRegistrationToken(hash, now + 72h)`.
5. Genera `TokenReference = Guid.NewGuid()`, guarda `TokenReference → raw` en Redis con TTL 30s.
6. Publica `OnboardingRegistrationReadyIntegrationEvent` con `TokenReference` (sin raw).
7. Commit atómico: paso 2 + paso 4 + escribir outbox del evento en la misma UoW #3.

**Endpoint M2M** `GET /auth/internal/onboarding/tokens/{reference}/raw`:
- ServiceOnly + `[AllowActorTypes(Service)]`.
- Redis: `GETDEL` para one-shot (al leer se borra).
- Devuelve `{registrationUrl}` = URL completa `https://{appBaseUrl}/register?token=<RAW>`.

**Cambios prohibidos**: fuera de Auth.

---

## Fase 10 — Documents: OnboardingReceipt DocumentType

**Objetivo**: extender Documents para generar el PDF del recibo del pago de onboarding.

**Archivos a modificar** (Documents):
- `Documents.Domain/ValueObjects/ValueObjects.cs`: `DocumentType` sigue siendo `string`; agregar constante `DocumentTypes.OnboardingReceipt = "OnboardingReceipt"` en un archivo dedicado.
- `Documents.Application/Abstractions/Abstractions.cs`: agregar `OnboardingReceiptPayload` DTO con `{OnboardingId, PayerFirstName, PayerLastName, PayerEmail, PlanName, PlanCode, PricePaid (Money), PaidAtUtc, TransactionReferenceMask (últimos 4), PaymentMethodMasked (ej "Visa …4242"), IssuerData (IssuerSnapshot fija de plataforma)}`.

**Archivos a crear**:
- `Documents.Application/Generations/OnboardingReceipt/GenerateOnboardingReceiptDocumentCommand.cs` + Handler
- `Documents.Application/Generations/OnboardingReceipt/ProcessOnboardingReceiptGenerationHandler.cs`
- `Documents.Infrastructure/Rendering/EmbeddedDocumentTemplates.cs`: agregar template `onboarding.receipt.v1` (HTML Fluid).
- `Documents.Api/Controllers/InternalOnboardingReceiptsController.cs`: `POST /internal/document-generations/onboarding-receipts` (ServiceOnly).
- `Documents.Application/Abstractions/PlatformIssuerOptions.cs` (options binder para `Documents:PlatformIssuer:*`).
- `Documents.Infrastructure/PlatformIssuer/PlatformIssuerProvider.cs`.

**Config nueva** en `appsettings.json`:
```json
"Documents": {
  "PlatformIssuer": {
    "Name": "TaxVision Inc.",
    "TaxId": "XX-XXXXXXX",
    "AddressLine1": "…",
    "City": "…", "State": "…", "PostalCode": "…", "Country": "US",
    "Phone": "…",
    "Email": "billing@taxvision.com",
    "Website": "https://taxvision.com",
    "LogoDataUri": null
  }
}
```

**Template Fluid** (embebido en `EmbeddedDocumentTemplates`): HTML minimal con `{{payerFirstName}}`, `{{payerLastName}}`, `{{planName}}`, `{{pricePaidFormatted}}`, `{{paidAtFormatted}}`, `{{transactionReferenceMask}}`, `{{paymentMethodMasked}}`, `{{issuer.name}}`, `{{issuer.taxId}}`, `{{issuer.address}}`, etc. Layout email-safe, PDF-A ready.

**Endpoint**:
- `POST /internal/document-generations/onboarding-receipts` recibe `OnboardingReceiptPayload` + `IdempotencyKey` en header. Handler crea `DocumentGeneration` con `Owner={Type=Onboarding, Id=OnboardingId}`, `DocumentType=OnboardingReceipt`, `TemplateKey=onboarding.receipt.v1`. Storage vía patrón existente `SaveFileRequestedIntegrationEvent`.

**Tests**: renderizar payload de ejemplo, verificar PDF válido, idempotencia por key.

**Cambios prohibidos**: Auth, PaymentApp, Tenant, Subscription, Billing, Scribe, Notification, Postmaster, CloudStorage.

---

## Fase 11 — Auth: cliente M2M a Documents

**Objetivo**: Auth invoca Documents post-pago para generar el recibo PDF, y publica evento con `ReceiptFileId` cuando llega el `DocumentGenerationCompleted`.

**Archivos a crear**:
- `Auth.Application/Onboarding/Abstractions/IReceiptDocumentClient.cs`
- `Auth.Infrastructure/Onboarding/HttpClients/ReceiptDocumentClient.cs` (M2M contra Documents, con timeout + retry)
- `Auth.Application/Onboarding/Consumers/OnboardingReceiptGenerationCompletedConsumer.cs` (consume `DocumentGenerationCompletedIntegrationEvent` filtrado por `OwnerType=Onboarding`)
- `BuildingBlocks/Messaging/AuthIntegrationEvents/OnboardingReceiptReadyIntegrationEvent.cs` `{OnboardingId, ReceiptFileId, ReceiptDownloadUrl, CorrelationId}`

**Modificar**:
- `Auth.Application/Onboarding/Consumers/OnboardingPaymentSucceededConsumer.cs`: tras publicar `OnboardingRegistrationReady`, invoca `IReceiptDocumentClient.RequestReceiptGenerationAsync(payload)` (fire-and-forget, la respuesta llega vía evento).

**Cambios prohibidos**: Documents, Billing, Notification/Scribe/Postmaster (F12).

---

## Fase 12 — Scribe + Notification: 2 templates + consumers

**Objetivo**: agregar 2 templates de email nuevos + 2 consumers en Notification.

**Templates Scribe** (agregar a `NotificationTemplateSeedSource.All`):
- `onboarding.otp_code` (EventKey `onboarding.otp_requested.v1`): HTML Fluid con `{{otpCode}}` en tamaño grande + `{{firstName|default:'there'}}` + `{{expiresInMinutes}}` + footer layout `system-base`.
- `onboarding.registration_ready` (EventKey `onboarding.registration_ready.v1`): HTML Fluid con `{{firstName}}, {{planName}}, {{priceFormatted}}, {{paidAtFormatted}}, {{transactionReferenceMask}}`, botón "Complete your account" con `href="{{registrationUrl}}"`, y (si `{{receiptDownloadUrl}}`) botón secundario "Download receipt". Layout `system-base`.

**Consumers Notification** en `Notification.Application/Consumers/OnboardingEventConsumers.cs`:
- `OnboardingOtpRequestedConsumer` — mapea variables → `scribeClient.RenderAsync("onboarding.otp_requested.v1", tenantId=null, variables)` → `gateway.QueueEmailAsync(...)`. Category `AccountSecurity`. Provider `System`.
- `OnboardingRegistrationReadyConsumer` — llama `IOnboardingClient.ResolveTokenReferenceAsync(evt.TokenReference)` (M2M HTTP a Auth `GET /auth/internal/onboarding/tokens/{ref}/raw`) para obtener `registrationUrl`. Si `OnboardingReceiptReady` ya llegó, incluir `receiptDownloadUrl`. Scribe → gateway. Category `Billing`. Provider `System`. Marca `IsCritical=true` para bypass futuro de preferencias.

**Cambios permitidos**: Scribe seeds, Notification consumers, `IOnboardingClient` en Notification.
**Cambios prohibidos**: Auth, PaymentApp, Documents, Tenant, Subscription, Billing, Postmaster, CloudStorage.

---

## Fase 13 — Auth: endpoints finales del registro

**Objetivo**: endpoints públicos que canjean el token, muestran el form, aceptan submit, exponen status.

**Archivos a crear**:
- `Auth.Api/Controllers/OnboardingRegistrationController.cs`:
  - `POST /onboarding/register/preview {token}` → `{firstName, lastName, maskedEmail, planName}` (Anonymous, throttled 30/min por IP)
  - `POST /onboarding/register/complete {token, password, officeName, subdomain, termsAccepted, termsVersionId}` con header `Idempotency-Key` obligatorio → `202 Accepted` + `{status, statusUrl}` (Anonymous, throttled 10/min por IP)
- `Auth.Api/Controllers/OnboardingStatusController.cs`:
  - `GET /onboarding/status?token=...` (Anonymous, throttled 60/min por IP) → `{status, currentStep?, failureReason?, failureCode?, redirectUrl? (si Completed)}`. NUNCA `OnboardingId`.
- `Auth.Application/Onboarding/Registration/Commands/CompleteOnboardingRegistrationCommand.cs` + Handler
- `Auth.Application/Onboarding/Registration/Queries/PreviewRegistrationQuery.cs`
- `Auth.Application/Onboarding/Registration/Queries/GetOnboardingStatusQuery.cs`
- Validators: password ≥ 12 chars, subdomain regex, terms=true.

**Handler `CompleteOnboardingRegistrationCommand`** (pipeline de validadores):
1. Resolver token → busca `TenantOnboarding` por `RegistrationTokenHash = SHA256(token)`. Valida `Status=RegistrationPending + RegistrationTokenExpiresAt>NOW + RegistrationTokenUsedAt IS NULL`.
2. Validar `TermsVersionId` está vigente. Obtener `ContentHash`.
3. Validar subdomain vía Fase 14.
4. `TenantOnboarding.StartProvisioning(officeName, subdomain, termsVersionId, contentHash, remoteIp, userAgent)`.
5. Publica `OnboardingProvisioningStartedIntegrationEvent {OnboardingId, TenantOnboardingSnapshot (sin password), PasswordCarrier (in-memory-only field marked [DoNotSerialize])}`.
6. Password **NUNCA persiste en `TenantOnboarding`, NUNCA se logea**. Se pasa a la Saga como variable en la request de Wolverine (Wolverine soporta metadata in-memory).
7. Devuelve `202 Accepted + {status:"Provisioning", statusUrl:"/onboarding/status?token=..."}`.

**Cambios prohibidos**: fuera de Auth.

---

## Fase 14 — Auth: SubdomainReservation en módulo Onboarding

**Objetivo**: reserva de subdominio post-pago con TTL 60min.

**Decisión de diseño a validar en esta fase**:
- **Opción A**: crear nueva tabla `Onboarding.SubdomainReservations` en el módulo Onboarding.
- **Opción B**: extender `TenantSubdomainReservation` existente con `OnboardingId? Guid NULL`.

Evaluar en la fase leyendo el aggregate existente. Recomendación tentativa: Opción A para respetar módulo, salvo que la Opción B sea trivial (columna nueva + IX opcional).

**Archivos a crear**:
- `Auth.Domain/Onboarding/SubdomainReservations/OnboardingSubdomainReservation.cs` (o modificar el existente si Opción B)
- `Auth.Domain/Onboarding/ValueObjects/SubdomainSlug.cs` (reusar VO existente en Auth si está publicado; si no, crear en el módulo Onboarding)
- `Auth.Application/Onboarding/SubdomainReservations/Queries/CheckSubdomainAvailabilityQuery.cs` (consulta local + M2M callback a Tenant `GET /tenants/internal/subdomain-available?slug=`)
- `Auth.Application/Onboarding/SubdomainReservations/Commands/ReserveSubdomainForOnboardingCommand.cs`
- `Auth.Api/Controllers/OnboardingSubdomainController.cs`
- Migración correspondiente (A: `AddOnboardingSubdomainReservations`; B: `AddOnboardingIdToTenantSubdomainReservations`).

**Endpoint**:
- `POST /onboarding/subdomains/check {slug, onboardingId?}` (Anonymous, rate-limited) → `{available, suggestedAlternatives?}`.

**Reserved words** (lista): `api, admin, www, mail, app, taxpro, help, support, status, docs, static, cdn, blog, marketing`.

**Concurrencia**: unique filtered index `(Slug) WHERE ConsumedAtUtc IS NULL AND ExpiresAtUtc > SYSUTCDATETIME()`.

**Cambios permitidos**: Auth (módulo Onboarding). Tenant: nuevo endpoint `GET /tenants/internal/subdomain-available?slug=` (ServiceOnly) que responde `{taken: bool}` consultando `Tenant.SubDomain`.

---

## Fase 15 — Auth: Wolverine Saga (Process Manager)

**Objetivo**: implementar el orquestador que coordina los 6 pasos remotos.

**Archivos a crear**:
- `Auth.Application/Onboarding/Sagas/TenantOnboardingProcessManager.cs` (Wolverine `Saga`)
- `Auth.Application/Onboarding/Sagas/Commands/CreateTenantForOnboardingCommand.cs`
- `Auth.Application/Onboarding/Sagas/Commands/CreateTenantOwnerCommand.cs`
- `Auth.Application/Onboarding/Sagas/Commands/ActivateSubscriptionCommand.cs`
- `Auth.Application/Onboarding/Sagas/Commands/ProvisionStorageForTenantCommand.cs`
- `Auth.Application/Onboarding/Sagas/Commands/ActivateSubdomainForTenantCommand.cs`
- `Auth.Application/Onboarding/Sagas/Commands/ConfigureTenantDefaultsCommand.cs`
- `Auth.Infrastructure/Onboarding/HttpClients/TenantProvisioningClient.cs` (M2M)
- `Auth.Infrastructure/Onboarding/HttpClients/AuthInternalOwnerCreationClient.cs` (M2M interno al mismo servicio? sí — porque el password no debe cruzar bus; el handler local recibe la request via HTTP loopback)
- `Auth.Infrastructure/Onboarding/HttpClients/SubscriptionActivationClient.cs` (M2M)
- `BuildingBlocks/Messaging/AuthIntegrationEvents/OnboardingProvisioning*IntegrationEvents.cs` (Started, Failed, Completed)
- Wolverine wiring en `Program.cs` para persistencia de saga (SQL Server).

**Saga**:
- `CorrelationId = OnboardingId`.
- Estado: `CurrentStep`, `TenantId?`, `UserId?`, `SubscriptionId?`, `PasswordCarrier` (con `[JsonIgnore]` para no persistir).
- Handler `Handle(OnboardingProvisioningStartedIntegrationEvent evt)` — arranca saga, guarda `PasswordCarrier=evt.Password`, envía `CreateTenantForOnboardingCommand`.
- Handler `Handle(TenantCreatedForOnboardingIntegrationEvent evt)` — actualiza `TenantId`, envía `CreateTenantOwnerCommand`.
- Handler `Handle(TenantOwnerCreatedIntegrationEvent evt)` — actualiza `UserId`, **destruye `PasswordCarrier`**, envía `ActivateSubscriptionCommand`.
- Handler `Handle(SubscriptionActivatedForOnboardingIntegrationEvent evt)` — actualiza `SubscriptionId`, envía `ProvisionStorageForTenantCommand`.
- Handlers análogos para storage/subdomain/defaults.
- Al llegar el último `TenantDefaultsConfiguredIntegrationEvent`: UoW #8 → `TenantOnboarding.MarkCompleted() + ConsumeRegistrationToken() + publish TenantOnboardingCompleted`, saga marcada `IsCompleted=true`.
- En cualquier `*Failed` intermedio: `MarkProvisioningFailed(step, code, reason)` + retry Polly (transient) o `OnboardingManualReviewRequired` (permanent).

**Cambios permitidos en otros servicios**: se documentan en F16, este PR solo implementa la saga con stubs M2M clients contra endpoints que se crearán en F16.

**Tests**: saga completa happy path (mocked M2M), fallo en cada paso queda `ProvisioningFailed`, retry transient reintenta 3 veces con backoff, permanent va a ManualReview inmediato, password destruido tras handler #4, restart de servicio recupera saga desde persistencia SQL.

---

## Fase 16 — Tenant + Subscription: endpoints M2M

**Objetivo**: los endpoints M2M que la Saga necesita en Tenant y Subscription.

### Tenant

**Archivos a crear**:
- `Tenant.Api/Controllers/InternalTenantProvisioningController.cs`: `POST /tenants/internal/from-onboarding` (`[Authorize(Policy="ServiceOnly")] + [AllowActorTypes(Service)]`).
- `Tenant.Application/Tenants/Commands/CreateTenantFromOnboardingCommand.cs` + Handler.
- Migración `AddOnboardingIdToTenants` con unique filtered index `(OnboardingId) WHERE OnboardingId IS NOT NULL`.

**Handler**:
1. Callback M2M a Auth `GET /auth/internal/onboarding/{onboardingId}/status` para validar `Status IN (Provisioning) AND PaymentCompletedAt IS NOT NULL`.
2. Validación idempotente: si ya existe `Tenant.OnboardingId=X`, retornar existente.
3. Crear `Tenant`.
4. Publicar `TenantCreatedForOnboardingIntegrationEvent {OnboardingId, TenantId, Name, SubdomainSlug, AdminEmail, ...}` **además** del `TenantCreatedIntegrationEvent` normal (que agrega `OnboardingId?` como campo opcional).

**Endpoint auxiliar** para F14:
- `Tenant.Api/Controllers/InternalSubdomainController.cs`: `GET /tenants/internal/subdomain-available?slug=X` (ServiceOnly) → `{taken}`.

### Auth (endpoint interno de owner)

**Archivos a crear**:
- `Auth.Api/Controllers/InternalTenantOwnersController.cs`: `POST /auth/internal/tenants/{tenantId}/owners` (`[Authorize(Policy="ServiceOnly")] + [AllowActorTypes(Service)]`).
- `Auth.Application/Users/Commands/CreateTenantOwnerCommand.cs` (interno, invocado por Saga).

**Handler**:
1. `IPasswordHasher.HashPassword(passwordPlaintext)` en la **primera línea**.
2. Borra referencia local a `passwordPlaintext`.
3. Validación idempotente por `OnboardingId`.
4. Crea `User` con `EmailVerified=true` (ya verificado por OTP), asigna rol TenantAdmin.
5. Publica `TenantOwnerCreatedIntegrationEvent {OnboardingId, TenantId, UserId, Email}`.
6. Logger NO recibe `passwordPlaintext` en NINGÚN argumento. Verificar con test captura de Serilog.

Migración `AddOnboardingIdToUsers` con unique filtered index.

### Subscription

**Archivos a crear**:
- `Subscription.Api/Controllers/InternalSubscriptionActivationController.cs`: `POST /subscriptions/internal/activate-from-onboarding` (ServiceOnly).
- `Subscription.Application/Subscriptions/Commands/ActivateFromOnboardingCommand.cs` + Handler.

**Handler**:
1. Idempotente por `OnboardingId`.
2. Crear `TenantSubscription` en `Active` (NO `Trialing`), `CurrentPeriodStart=NOW`, `CurrentPeriodEnd=NOW+billingCycle`.
3. Publica `SubscriptionActivatedForOnboardingIntegrationEvent`.

Migración `AddOnboardingIdToTenantSubscriptions`.

**Archivos a modificar**:
- `Subscription.Application/Tenants/IntegrationEvents/TenantCreatedConsumer.cs:82`: si `evt.OnboardingId != null` → early return (no crear trial). Si `evt.OnboardingId == null` → comportamiento actual (arranca trial).
- `Auth.Application/Tenants/IntegrationEvents/TenantCreatedConsumer.cs:56-99`: si `evt.OnboardingId != null` → early return del bloque de Invitation creation (el TenantAdmin ya se creó vía Saga). Todo lo demás del consumer intacto.

**Extender** `TenantCreatedIntegrationEvent` en BuildingBlocks: agregar `OnboardingId? Guid`. Backwards-compat: campo opcional, consumers actuales lo ignoran.

**Tests**: cross-service integration test end-to-end desde `CompleteOnboardingRegistrationCommand` hasta `TenantOwnerCreatedIntegrationEvent`, con verificación de captura Serilog que confirme que `passwordPlaintext` no aparece en logs.

---

## Fase 17 — Compensaciones + ManualReview + observabilidad

**Objetivo**: retries clasificados + endpoints admin de ManualReview + métricas + logs estructurados.

**Archivos a crear**:
- `Auth.Application/Onboarding/Failures/FailureClassifier.cs` (mapea `FailureCode` a `{Transient|Permanent}`).
- `Auth.Api/Controllers/OnboardingAdminController.cs` (PlatformAdmin):
  - `GET /auth/onboarding/admin?status=ManualReview&page=...&limit=...`
  - `GET /auth/onboarding/admin/{id}` (detalle completo)
  - `POST /auth/onboarding/admin/{id}/resume`
  - `POST /auth/onboarding/admin/{id}/update-and-resume {subdomain?, planId?}`
  - `POST /auth/onboarding/admin/{id}/cancel-and-refund {reason, confirmation}`
  - `POST /auth/onboarding/admin/{id}/force-complete {reason}`
- `Auth.Application/Onboarding/Admin/Commands/*` (uno por endpoint admin).
- `Auth.Application/Onboarding/Consumers/OnboardingRefundConsumer.cs` en PaymentApp (nuevo consumer que llama Stripe Refund).
- OpenTelemetry: métricas `onboarding_started_total`, `onboarding_completed_total`, `onboarding_failed_total{step}`, `onboarding_duration_seconds{outcome}`, `onboarding_manual_review_total`.
- Health check: `GET /auth/health/detailed` que verifica DB, Redis, RabbitMQ, y downstream services (Documents, PaymentApp, Tenant, Subscription).

**Retry policies**:
- Transient: Polly 3 intentos (1s/5s/30s) → si agotado, retry background (5min/15min/1h) hasta 24h.
- Permanent: sin retry, ManualReview inmediato.

**Refund**:
- `POST /auth/onboarding/admin/{id}/cancel-and-refund` valida `Confirmation="I understand this is irreversible"`.
- Publica `OnboardingRefundRequestedIntegrationEvent` → PaymentApp consume → `IProviderPaymentAdapter.RefundAsync` con `IdempotencyKey`.
- Compensaciones: publica `OnboardingCancelRequestedIntegrationEvent {TenantId?, UserId?, SubscriptionId?}` → Subscription cancela, Auth deactiva user, Tenant marca `Closed`.

**Tests**: retry transient recovers, permanent no retry, admin resume happy path, refund publica evento a PaymentApp, force-complete queda auditado.

---

## Fase 18 — Credentials Hardening (Forgot Password + Refresh + Invitation)

**Objetivo**: blindar los flujos de credenciales existentes en Auth para todos los actor types. Trabajo separado del PayFlow pero convive con él.

**Deuda a resolver**:
1. `ForgotPasswordHandler` (`PasswordCommands.cs:24-78`) sin throttler → permite enumeración por timing + spam de emails.
2. `ResetPasswordCommand` (`PasswordCommands.cs:87`) sin rate limit por reset token attempt.
3. `RefreshAccessTokenCommand` (`RefreshAccessToken.cs:14`) sin binding host↔refresh token → un refresh de tenantA canjea en host de tenantB.
4. `AcceptInvitationHandler` (`AcceptInvitation.cs:19`) sin throttling → permite guessing del InvitationToken.
5. `MfaChallengeRequestedIntegrationEvent` viaja con OTP en claro por RabbitMQ (documentado, no cambia en este plan — a evaluar en plan separado).
6. `AdminInvitationRawToken` viaja dentro de `TenantCreatedIntegrationEvent` en Auth (`Auth.Application/Tenants/IntegrationEvents/TenantCreatedConsumer.cs:79-97`) — reemplazar con patrón `TokenReference` similar al Onboarding.

**Archivos a modificar / crear**:
- `Auth.Application/Credentials/Commands/PasswordCommands.cs`:
  - `ForgotPasswordHandler`: integrar `ILoginThrottler.IsPasswordResetRequestThrottledAsync(email, ip)` — max 3 por email/hora, max 10 por IP/hora, cooldown 60s.
  - Fail-open enumeration: SIEMPRE responder 202 tras rate limit check (no diferenciar email existente vs no existente).
  - Constant-time check para búsqueda del email en DB.
  - `ResetPasswordHandler`: rate limit por token attempt — max 5 intentos por token, tras eso invalidar token.
- `Auth.Application/Users/Commands/RefreshAccessToken.cs`:
  - Validar que el `RefreshToken` fue emitido para el host resuelto (comparar `refreshToken.TenantId` vs `tenantContext.CurrentTenantId`).
  - Si mismatch: `SecurityAlertIntegrationEvent {Kind=RefreshTokenHostMismatch}` + `SessionDenylist` add + 401.
- `Auth.Application/Invitations/Commands/AcceptInvitation.cs`:
  - Rate limit por IP: max 20 attempts/hora.
  - Rate limit por token: max 5 attempts, tras eso invalidar.
- `Auth.Application/Tenants/IntegrationEvents/TenantCreatedConsumer.cs:79-97`:
  - Refactor: en vez de recibir `AdminInvitationRawToken` en el evento, recibir solo `AdminInvitationTokenReference (Guid)`. El emisor (Tenant service) publica el evento con el reference, y separadamente hace M2M `POST /auth/internal/invitations/token-references` para depositar el raw en Redis (TTL 30s, one-shot). Consumer resuelve via `GET /auth/internal/invitations/token-references/{ref}`. Mismo patrón que el `TokenReference` del Onboarding.
- Extender `BuildingBlocks/Messaging/TenantIntegrationEvents/TenantCreatedIntegrationEvent.cs`: reemplazar `AdminInvitationRawToken (string)` por `AdminInvitationTokenReference (Guid?)`. Versión del evento sube (`.v2`).
- Migración de consumers actuales del evento en los otros 4 servicios (Signature, PaymentClient, PaymentApp, Subscription): ninguno lee el raw token, así que el cambio es solo en Auth.

**Tests**:
- ForgotPassword: rate limit por email, rate limit por IP, fail-open enumeration (mismo tiempo de respuesta para email existente vs no existente medido con benchmark).
- ResetPassword: max 5 intentos por token, cooldown resend.
- RefreshToken: host mismatch → 401 + SessionDenylist.
- AcceptInvitation: rate limit + cooldown.
- TenantCreated refactor: raw token no aparece en captura del bus.

**Cambios prohibidos**: fuera de Auth (excepto extensión del evento en BuildingBlocks).

**Riesgo**: refactor del `TenantCreatedIntegrationEvent` puede afectar consumers actuales. Testear cada consumer con el evento nuevo.

---

## Fase 19 — README + Postman + verificación E2E

**Objetivo**: documentación + colección Postman + `API_Contract.md` para el equipo frontend + verificación manual end-to-end.

**Archivos a crear/modificar**:
- `README.md`: sección nueva §XX PayFlow Onboarding con endpoints, eventos, tablas, flujo, config vars, docker-compose.
- `TaxVision_Onboarding.postman_collection.json` con todos los endpoints + environment vars nuevas.
- `documents/architecture/payflowguide/API_Contract.md` para frontend: cada endpoint con request/response de ejemplo, códigos de error, flujo secuencial paso a paso, matriz de estados posibles.
- `documents/architecture/payflowguide/ADR-001-Auth-Hosts-Onboarding.md` (ADR justificando por qué el bounded context Onboarding vive en Auth como módulo).
- `documents/architecture/payflowguide/ADR-002-Documents-Hosts-Receipt.md` (ADR justificando por qué el recibo vive en Documents).

**Verificación E2E manual**:
1. Crear challenge OTP → recibir email real → verificar código correcto y código incorrecto (max attempts).
2. Crear onboarding → recibir link Stripe → pagar con card test 4242… → recibir email recibo+token → click link.
3. Canjear token → completar registro → verificar tenant creado + user + subscription activa + login funciona en el subdomain nuevo.
4. Provocar fallo del paso Subscription (matar Subscription service) → verificar ManualReview + retry manual recupera.
5. Provocar fallo permanente (plan borrado) → verificar refund manual funciona.
6. Verificar que password NO aparece en logs Serilog (grep de `passwordPlaintext` en logs generados).

**Full monorepo**: `dotnet build TaxVision.slnx` + `dotnet test TaxVision.slnx` verde.

---

# Parte III — Anexos

## Anexo A — Checklist final de validación

Antes de considerar el plan implementado:

- [ ] Las 19 fases (F0-F19, incluye F18 credentials hardening) completadas en orden con dependencias respetadas.
- [ ] `TenantOnboardingId` NUNCA se expone al frontend en ningún endpoint público.
- [ ] `RegistrationToken` raw NUNCA se persiste; solo `SHA256(token)`; nunca por RabbitMQ (patrón TokenReference + Redis one-shot 30s).
- [ ] `Password` del TenantAdmin NUNCA por RabbitMQ; HTTP síncrono TLS + ServiceOnly + hasheado en primera línea del handler + no logeado + test de captura Serilog verifica.
- [ ] OTP tiene rate limit por email + por IP + cooldown resend + max attempts + expiración + hash salteado + constant-time compare.
- [ ] Webhook Stripe idempotente por `(ProviderCode, ProviderEventId)` — YA existe, reusable.
- [ ] `SaaSPayment` idempotente por `IdempotencyKey` — YA existe, reusable.
- [ ] Cada handler M2M de la Saga es idempotente por `OnboardingId` (unique filtered index).
- [ ] `TermsVersion` inmutable con `ContentHash` guardado también en `TenantTermsAcceptance` (defensa en profundidad).
- [ ] `TenantTermsAcceptance` con unique index `(TenantId, UserId, TermsVersionId)`.
- [ ] Reserva de subdomain se hace POST-pago.
- [ ] `Tenant.Status` NO cambia enum (el "PendingPayment" vive en `TenantOnboarding`, no en `Tenant`).
- [ ] `Subscription.TenantCreatedConsumer` es condicional (OnboardingId!=null → early return).
- [ ] `Auth.TenantCreatedConsumer` es condicional (OnboardingId!=null → no crea Invitation admin).
- [ ] `TenantCreatedIntegrationEvent` gana campo opcional `OnboardingId?` sin romper los 5 consumers actuales.
- [ ] 2 templates nuevas en Scribe seed (`onboarding.otp_code`, `onboarding.registration_ready`).
- [ ] 2 consumers nuevos en Notification.
- [ ] Nuevo `DocumentType="OnboardingReceipt"` en Documents con handler + template embebido.
- [ ] Postmaster sin cambios de código en MVP.
- [ ] Compensaciones: refund solo tras acción humana + `Confirmation="I understand..."`.
- [ ] F18 hardening cierra deuda preexistente: ForgotPassword throttler + RefreshToken host binding + AcceptInvitation throttling + AdminInvitationRawToken via TokenReference.
- [ ] Migración de `TenantTermsAcceptances` legacy con `Version='legacy-2026-07-14'` + `AcceptedInContext='LegacyPreV2'`.
- [ ] NetArchTest de Onboarding module en Auth verde.
- [ ] `dotnet build TaxVision.slnx` + `dotnet test TaxVision.slnx` verde antes y después de cada fase.
- [ ] Wolverine saga usa SQL Server persistence.
- [ ] Todos los endpoints M2M nuevos con `[Authorize(Policy="ServiceOnly")] + [AllowActorTypes(Service)]` + registrado en Auth `ServiceAuthOptions`.
- [ ] Tests de integración con Testcontainers (SQL Server + Rabbit + Redis) para la saga completa.
- [ ] Verificación manual end-to-end antes de merge (Fase 19).

## Anexo B — Matriz de fallas y recuperación

Ver Anexo C del PDF original. Copia integrada:

| Punto | Fallo | Acción | Garantía |
|---|---|---|---|
| Antes de crear onboarding | DB no disponible | Detener | No crear checkout; no hay obligación de pago |
| Checkout creado | Falla persistencia local | Recuperar | Reconciliar por CheckoutReference + OnboardingId |
| Webhook duplicado | Evento repetido | Ignorar seguro | Inbox / ProviderEventId |
| Después de PaymentCompleted | Email/Notification caído | Retry | Outbox; cliente no vuelve a pagar |
| Provisioning Tenant | Tenant Service falla | Retry / Stop | No crear usuario ni subscription hasta TenantCreated |
| Provisioning Subscription | Subscription falla | ProvisioningFailed | Retry automático; luego ManualReview si persiste |
| Infra externa | Cloudflare/Storage timeout | Retry | Fallo transitorio con backoff |
| Error permanente | Plan/configuración inconsistente | ManualReview | No retry infinito; soporte decide |
| Fallo no recuperable | No puede completarse servicio | Compensar | Deshabilitar tenant, cancelar recursos y/o refund |

## Anexo C — Glosario

| Término | Significado |
|---|---|
| **Bounded Context** | Frontera de lenguaje ubicuo y modelo (DDD Evans cap. 14). |
| **Modular Monolith** | Múltiples bounded contexts en un solo servicio con fronteras verificadas por tests (Vernon cap. 2). |
| **Saga / Process Manager** | Workflow persistido que coordina UoW distribuidas usando eventos y comandos idempotentes (Richardson pattern 4.2). |
| **Unit of Work (UoW)** | Transacción SQL local atómica dentro de un servicio (Fowler PEAA). |
| **Outbox Pattern** | Escribir el evento en la misma transacción del cambio de estado, y publicar después vía dispatcher (Richardson). |
| **Inbox Pattern** | Registrar eventos entrantes en tabla local antes de procesar para garantizar exactly-once (Kleppmann cap. 11). |
| **Idempotencia** | Ejecutar N veces = ejecutar 1 vez (misma respuesta, mismo estado final). |
| **Compensación** | Acción semánticamente inversa cuando no hay rollback (Cancel subscription, Disable tenant, Refund payment). |
| **Fail-open** | Ante duda, permitir la operación (usado en checks de enumeración: no diferenciar por tiempo respuestas email existente/no). |
| **Fail-closed** | Ante duda, denegar (usado en rate limiter: si Redis falla, no dejar pasar). |
| **CorrelationId** | Identificador que atraviesa todos los eventos/logs de un mismo proceso lógico (aquí = `OnboardingId`). |
| **CausationId** | Identificador del evento que causó otro evento. |
| **Capability Token** | JWT con `purpose` claim que autoriza una operación específica sin identidad de usuario (ej. `TenantRegistrationTicket`). |

## Anexo D — Referencias a archivos y líneas del repo

**Auth actual**:
- `src/Services/Auth/TaxVision.Auth.Application/TenantDomains/Commands/ReserveSubdomain.cs:36` — `ReserveSubdomainHandler`
- `src/Services/Auth/TaxVision.Auth.Application/Tenants/IntegrationEvents/TenantCreatedConsumer.cs:17,56-99,174` — proto-orquestador implícito, crea Invitation admin, consume reserva
- `src/Services/Auth/TaxVision.Auth.Application/Invitations/Commands/AcceptInvitation.cs:19,101` — accept invitation, verify email implícito
- `src/Services/Auth/TaxVision.Auth.Application/Credentials/Commands/PasswordCommands.cs:24-78,87,171` — ForgotPassword/ResetPassword/ChangePassword
- `src/Services/Auth/TaxVision.Auth.Application/Users/Commands/Login.cs:319,328` — genera MFA OTP + publica evento con OTP en claro
- `src/Services/Auth/TaxVision.Auth.Application/Users/Commands/RefreshAccessToken.cs:14` — refresh sin binding host
- `src/Services/Auth/TaxVision.Auth.Application/Terms/Commands/AcceptTerms.cs:17,33,59` — accept terms, TenantEntity requires TenantId
- `src/Services/Auth/TaxVision.Auth.Application/Terms/TermsOptions.cs:8` — CurrentVersion hardcoded
- `src/Services/Auth/TaxVision.Auth.Domain/Terms/TenantTermsAcceptance.cs:11-19` — aggregate actual, campos mínimos
- `src/Services/Auth/TaxVision.Auth.Infrastructure/Configurations/TermsConfigurations.cs:12-24` — EF config actual
- `src/Services/Auth/TaxVision.Auth.Domain/TenantDomains/TenantSubdomainReservation.cs:13,41-49` — reserva actual TTL 15min
- `src/Services/Auth/TaxVision.Auth.Application/TenantDomains/JwtTokenGenerator.cs:113` — GenerateTenantRegistrationTicket
- `src/Services/Auth/TaxVision.Auth.Domain/Onboarding/*` — MÓDULO NUEVO A CREAR

**PaymentApp actual**:
- `src/Services/PaymentApp/TaxVision.PaymentApp.Domain/SaaSPayments/SaaSPayment.cs:77,222-223,232-237` — Create, MarkPaid con ReceiptNumber/Hash (no relacionado con receipt PDF)
- `src/Services/PaymentApp/TaxVision.PaymentApp.Domain/SaaSPayments/PaymentStatus.cs`, `SaaSPaymentType.cs`
- `src/Services/PaymentApp/TaxVision.PaymentApp.Api/Controllers/StripeWebhookController.cs:22` — webhook Anonymous + HMAC
- `src/Services/PaymentApp/TaxVision.PaymentApp.Application/Webhooks/ProcessStripeWebhookHandler.cs:65,112` — Inbox check + branch por tipo
- `src/Services/PaymentApp/TaxVision.PaymentApp.Infrastructure/Providers/Stripe/StripePaymentAdapter.cs:19,189,328` — Stripe SDK real, IdempotencyKey, VerifyWebhookSignature
- `src/Services/PaymentApp/TaxVision.PaymentApp.Infrastructure/Persistence/Configurations/SaaSPaymentConfiguration.cs:24` — UX_SaaSPayments_IdempotencyKey
- `src/Services/PaymentApp/TaxVision.PaymentApp.Infrastructure/Persistence/Configurations/WebhookEventConfiguration.cs:27` — UX_WebhookEvents_ProviderCode_ProviderEventId

**Documents actual**:
- `src/Services/Documents/TaxVision.Documents.Api/Controllers/InternalDocumentGenerationsController.cs:34` — POST /internal/document-generations/invoices (ServiceOnly)
- `src/Services/Documents/TaxVision.Documents.Domain/Generations/DocumentGeneration.cs:15` — aggregate
- `src/Services/Documents/TaxVision.Documents.Domain/ValueObjects/ValueObjects.cs:7-73` — DocumentType (string), TemplateKey, StorageReference
- `src/Services/Documents/TaxVision.Documents.Infrastructure/Rendering/Renderers.cs:18,67` — TemplateDocumentRenderer (Fluid), PlaywrightHtmlToPdfConverter
- `src/Services/Documents/TaxVision.Documents.Infrastructure/Rendering/EmbeddedDocumentTemplates.cs:9,13,25` — biblioteca embebida, único template `billing.invoice.v1`
- `src/Services/Documents/TaxVision.Documents.Infrastructure/Storage/DocumentStorageClient.cs:28,65` — patrón Fase D0 con SaveFileRequestedIntegrationEvent

**Tenant actual**:
- `src/Services/Tenant/TaxVision.Tenant.Api/Controllers/TenantController.cs:44,73,98` — POST/GET/PATCH tenants
- `src/Services/Tenant/TaxVision.Tenant.Domain/Tenant.cs:10,53` — aggregate, arranca Active
- `src/Services/Tenant/TaxVision.Tenant.Api/Common/EffectiveTenantRegistrationResolver.cs:17` — capability token resolver

**Subscription actual**:
- `src/Services/Subscription/TaxVision.Subscription.Application/Tenants/IntegrationEvents/TenantCreatedConsumer.cs:82,104-114` — crea trial (regresión conocida OrThrow)
- `src/Services/Subscription/TaxVision.Subscription.Domain/Subscriptions/TenantSubscription.cs:16` — aggregate
- `src/Services/Subscription/TaxVision.Subscription.Api/Controllers/SubscriptionsController.cs:86` — POST /subscriptions/activate manual
- `src/Services/Subscription/TaxVision.Subscription.Infrastructure/Scheduling/TrialExpirationJob.cs:11-14` — solo expira, no cobra

**Billing actual** (CERO cambios en este plan):
- `src/Services/Billing/TaxVision.Billing.Domain/Invoices/Invoice.cs:15,27,202,222-223,232-237` — Invoice + Customer + MarkPaid
- `src/Services/Billing/TaxVision.Billing.Application/Invoices/IntegrationEvents/InvoicePaymentSucceededConsumer.cs:23` — consume PaymentClient event
- `src/Services/Billing/TaxVision.Billing.Application/Invoices/IntegrationEvents/DocumentGenerationCompletedConsumer.cs:17` — consume Documents event
- `src/Services/Billing/TaxVision.Billing.Infrastructure/Documents/BillingDocumentsClient.cs:75` — M2M a Documents

**Notification + Scribe + Postmaster actuales**:
- `src/Services/Notification/TaxVision.Notification.Application/Common/EventBasedEmailDispatchGateway.cs:23,30,75` — gateway con idempotencia por RelatedEventId
- `src/Services/Notification/TaxVision.Notification.Application/Common/NotificationDispatcher.cs:209-223` — gate de preferencias solo para SMS/Push/InApp (email pasa siempre)
- `src/Services/Scribe/TaxVision.Scribe.Api/Controllers/RenderController.cs:21` — POST /scribe/render (ServiceOnly)
- `src/Services/Scribe/TaxVision.Scribe.Application/Templates/Seed/NotificationTemplateSeedSource.cs:29,35-50,139-170,274-306,463` — 13 templates seed (incluye `auth.otp_code` para MFA login)
- `src/Services/Postmaster/TaxVision.Postmaster.Application/Consumers/NotificationsEmailSendRequestedConsumer.cs:51,237,291,401` — consumer con suppression + rate limit + idempotency
- `src/Services/Postmaster/TaxVision.Postmaster.Infrastructure/Idempotency/SqlIdempotencyGuard.cs:17,23,48` — Idempotency 7d TTL + 30s retry window

---

**FIN DEL DOCUMENTO MAESTRO.** Cualquier duda arquitectónica, contrato, o decisión debe resolverse leyendo este .md antes de tocar código. Actualizaciones al documento se hacen en el mismo archivo con nota `[UPDATE YYYY-MM-DD]` en la sección afectada.
