# PayFlow — API Contract (frontend)

> Este documento cubre dos contratos independientes, cada uno con su propio índice: **PayFlow**
> (onboarding pay-first, abajo) y **Billing** (facturación — ver [sección al final](#billing--api-contract-frontend)).
> Se mantienen en el mismo archivo por decisión explícita del usuario; no comparten flujo ni dominio.

Contrato completo del flujo de onboarding "pay-first" para el equipo de frontend. Todos los endpoints
son de `TaxVision.Auth` salvo donde se indica. Todos los endpoints listados acá son **anónimos**
(sin `Authorization` header) salvo `POST auth/onboarding/terms/publish`, que es solo para el panel
admin de PlatformAdmin y no forma parte del flujo público.

Base URL: `{{UrlBase}}` (Gateway). Todas las rutas están relativas a esa base, sin prefijo `/api`.
El Gateway rutea `/onboarding/**` → `Auth` (ver `ReverseProxy.Routes.onboarding` en
`src/Gateway/TaxVision.Gateway/appsettings.json`) además de `/auth/**`, ya que los 6 controllers de
este módulo usan el prefijo bare `onboarding/...` en vez de `auth/onboarding/...` (única excepción:
`TermsVersionsController`, bajo `auth/onboarding/terms`, cubierto por la ruta `auth`).

## Índice

1. [El flujo, paso a paso](#1-el-flujo-paso-a-paso)
2. [Endpoints](#2-endpoints)
3. [Matriz de estados (`TenantOnboardingStatus`)](#3-matriz-de-estados-tenantonboardingstatus)
4. [Códigos de error](#4-códigos-de-error)
5. [Invariantes de seguridad](#5-invariantes-de-seguridad)
6. [Verificación E2E manual (checklist previo a merge)](#6-verificación-e2e-manual-checklist-previo-a-merge)

---

## 1. El flujo, paso a paso

```
1. Frontend pide un OTP           →  POST onboarding/email-challenges
2. Usuario ingresa el código      →  POST onboarding/email-challenges/{id}/verify
3. Frontend crea el onboarding    →  POST onboarding                              → onboardingId
4. Frontend arranca el checkout   →  POST onboarding/checkout                     → checkoutUrl (Stripe)
5. Browser redirige a Stripe Checkout Session (fuera de TaxVision)
6. Stripe redirige de vuelta a successUrl/cancelUrl (definidos en el paso 4)
7. [Async] Webhook de Stripe → PaymentApp → Auth activa el onboarding → email de "completa tu registro"
8. Usuario hace click en el link del email  →  frontend llama:
   8a. POST onboarding/register/preview       (prellenar el form)
   8b. GET  auth/onboarding/terms/current      (mostrar términos vigentes)
   8c. POST onboarding/subdomains/check        (validar+reservar el subdominio elegido)
   8d. POST onboarding/register/complete       → arranca el provisioning (Saga)
9. Frontend hace poll de:            GET onboarding/status?token=...
   hasta Status="Completed" (o ManualReview/ProvisioningFailed)
10. Cuando Completed: redirectUrl trae el subdominio nuevo → login normal ahí
11. [Async, en paralelo desde el paso 7] Email de recibo con link de descarga:
    GET onboarding/receipts/{fileId}/download
```

El `token` del paso 3-4 (`onboardingId`) **no es** el mismo `token` de los pasos 8-9 (ese es el
`RegistrationToken` que llega por email tras el pago — el frontend nunca lo genera, solo lo recibe
en la URL del link de email y lo reenvía tal cual).

## 2. Endpoints

### 2.1 `POST onboarding/email-challenges`

Pide un código OTP de 6 dígitos al email indicado.

**Request**
```json
{ "email": "buyer@example.com", "firstNameHint": "Ada" }
```
`firstNameHint` es opcional (se usa solo para personalizar el asunto del email de OTP).

**Response 201**
```json
{ "challengeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6" }
```

**Errores**: `Onboarding.OtpRateLimited` (400 — 5/email/hora o 10/IP/hora superados),
`Onboarding.Email` (400 — formato inválido).

---

### 2.2 `POST onboarding/email-challenges/{challengeId}/verify`

**Request**
```json
{ "code": "482913" }
```

**Response**: `204 No Content`.

**Errores**: `Onboarding.ChallengeNotFound` (404), `Onboarding.OtpExpired` (400 — TTL 10min
vencido), `Onboarding.OtpLocked` (400 — 5 intentos fallidos agotados), `Onboarding.OtpMismatch`
(400 — código incorrecto, cuenta como intento fallido).

Verificar dos veces con el mismo código correcto es idempotente (`Onboarding.AlreadyVerified` no se
devuelve como error — un replay del mismo código ya verificado responde 204 igual).

---

### 2.3 `POST onboarding/email-challenges/{challengeId}/resend`

Sin body. Reenvía un código nuevo (invalida el anterior).

**Response**: `202 Accepted`.

**Errores**: `Onboarding.ChallengeNotFound` (404), `Onboarding.ResendCooldown` (400 — esperar 60s
desde el último envío), `Onboarding.ResendLimitExceeded` (400 — máximo 5 reenvíos por challenge).

---

### 2.4 `POST onboarding`

Crea el `TenantOnboarding`. Requiere que `emailVerificationChallengeId` corresponda a un challenge
ya verificado (paso 2.2) con el mismo email.

**Request**
```json
{
  "email": "buyer@example.com",
  "firstName": "Ada",
  "lastName": "Lovelace",
  "phone": "+18095551234",
  "planId": "5b1f7c2a-0000-0000-0000-000000000001",
  "emailVerificationChallengeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```
`phone` es opcional (`null` si se omite).

**Response 201**
```json
{
  "onboardingId": "8c2e1a90-1111-2222-3333-444455556666",
  "email": "buyer@example.com",
  "planId": "5b1f7c2a-0000-0000-0000-000000000001"
}
```

⚠️ **Este es el único endpoint público que devuelve `onboardingId` en claro** — el frontend lo
necesita en memoria (no persistido, no en la URL) durante el resto de esta misma sesión de compra
para los pasos 2.5 y (opcionalmente) 2.6. Ningún endpoint posterior a un pago exitoso vuelve a
exponerlo: `register/preview`, `register/complete` y `status` trabajan solo con tokens/URLs opacas.

**Errores**: `Onboarding.ChallengeNotFound` (404), `Onboarding.ChallengeEmailMismatch` (400),
`Onboarding.EmailNotVerified` (400), `Onboarding.Name`/`Onboarding.Email`/`Onboarding.Plan` (400 —
validación de campos).

---

### 2.5 `POST onboarding/checkout`

Arranca la Stripe Checkout Session. Requiere el `onboardingId` de 2.4.

**Request**
```json
{
  "onboardingId": "8c2e1a90-1111-2222-3333-444455556666",
  "payerEmail": "buyer@example.com",
  "successUrl": "https://taxvision.com/onboarding/success?session_id={CHECKOUT_SESSION_ID}",
  "cancelUrl": "https://taxvision.com/onboarding/cancelled"
}
```

**Response 200**
```json
{
  "paymentId": "a1b2c3d4-0000-0000-0000-000000000000",
  "checkoutUrl": "https://checkout.stripe.com/c/pay/cs_test_...",
  "expiresAtUtc": "2026-07-29T10:15:00Z"
}
```

El frontend hace `window.location.href = checkoutUrl` — la sesión de Stripe expira en 24h.

**Errores**: `Onboarding.NotFound` (404), `Onboarding.InvalidState` (400 — el onboarding ya no está
en `PendingPayment`), errores `502`/`503` si PaymentApp o Stripe fallan.

---

### 2.6 `POST onboarding/subdomains/check`

Valida formato/disponibilidad del subdominio elegido y **lo reserva por 60 minutos** (a pesar del
nombre "check"). Se llama en la pantalla de registro (paso 8), no en el checkout.

El onboarding se resuelve **server-side** a partir del mismo `token` opaco que usan 2.7 y 2.8 — el
mismo hash-lookup contra `RegistrationTokenHash` (`Onboarding.InvalidToken`/`TokenUsed`/
`TokenExpired`). El endpoint nunca recibe `onboardingId` ni `email` del cliente: el invariante §5
(el `onboardingId` real se expone una única vez, en la respuesta de `POST onboarding`, y sólo vive
en memoria de esa sesión de compra) hacía que este endpoint fuera estructuralmente imposible de
llamar cuando el link de registro llega por email después del webhook async de Stripe — en ese
momento el comprador puede estar en otra pestaña, otro dispositivo o incluso otro día, sin ningún
`onboardingId` en memoria. `email` tampoco se acepta: se usa `onboarding.Email` ya validado, evitando
que el cliente mande un email arbitrario para la reserva.

**Request**
```json
{ "slug": "freedomtax", "token": "9f8e7d6c5b4a...raw-token-from-email-link" }
```

**Response 200**
```json
{ "available": true, "reason": null, "expiresAtUtc": "2026-07-29T11:15:00Z" }
```
o si no está disponible:
```json
{ "available": false, "reason": "TAKEN", "expiresAtUtc": null }
```

Llamar de nuevo con el mismo `token` + `slug` renueva la reserva (idempotente) — útil si el usuario
tarda en completar el form.

**Errores**: `Onboarding.InvalidToken`/`TokenUsed`/`TokenExpired` (400, mismos que 2.7/2.8),
`Onboarding.SubdomainTaken` (400), formato inválido de slug (400, código de `SubdomainSlug` VO),
`Onboarding.SubdomainReservedTemporarily` (400 — otro onboarding tiene el slug reservado, sea por
este mismo flujo o por el flujo de alta directa vía `TenantDomains`).

---

### 2.7 `POST onboarding/register/preview`

Resuelve el `RegistrationToken` (del link del email de recibo) para prellenar el form.

**Request**
```json
{ "token": "9f8e7d6c5b4a...raw-token-from-email-link" }
```

**Response 200**
```json
{ "firstName": "Ada", "lastName": "Lovelace", "maskedEmail": "b***@example.com", "planName": "Pro" }
```
`planName` puede ser `null` en casos legacy sin plan asociado.

**Errores**: `Onboarding.InvalidToken` (400), `Onboarding.TokenUsed` (400 — ya se completó el
registro), `Onboarding.TokenExpired` (400 — TTL 72h vencido).

---

### 2.8 `POST onboarding/register/complete`

Canjea el token, crea el password del owner y **arranca el provisioning** (la Saga, §44.3 del
README). Este es el paso más sensible del flujo — el `password` se hashea en la primera línea del
handler y nunca se loguea ni cruza RabbitMQ en claro.

**Request**
```json
{
  "token": "9f8e7d6c5b4a...raw-token-from-email-link",
  "password": "Str0ng-P@ssw0rd!",
  "officeName": "FreedomTax LLC",
  "subdomain": "freedomtax",
  "termsAccepted": true,
  "termsVersionId": "1a2b3c4d-0000-0000-0000-000000000001"
}
```
`subdomain` debe coincidir con uno reservado en 2.6 para este mismo onboarding (si no, `409`/`400`
`Onboarding.SubdomainNotReserved`). `termsVersionId` debe ser el `TermsVersionId` devuelto por
`GET auth/onboarding/terms/current` (§2.11) — no un valor arbitrario.

**Response 202**
```json
{ "status": "Provisioning", "statusUrl": "/onboarding/status?token=9f8e7d6c5b4a..." }
```
El **mismo** `token` del request sirve para el polling de estado (§2.9) — no se emite un token
nuevo.

**Errores**: `Onboarding.InvalidToken`/`TokenUsed`/`TokenExpired` (mismos que preview),
`Onboarding.TermsNotAccepted` (400), `Onboarding.SubdomainNotReserved` (400), `TermsVersion.NotFound`
(404), `Onboarding.TermsVersionNotCurrent` (400 — se publicó una versión más nueva mientras el
usuario llenaba el form; el frontend debe volver a pedir `terms/current`), password que no cumple
la política (`Password.*`, 400).

---

### 2.9 `GET onboarding/status?token=...`

Polling del progreso post-submit. El frontend debería llamarlo cada 2-3 segundos mientras
`status` no sea terminal.

**Response 200** (en progreso)
```json
{ "status": "Provisioning", "currentStep": "Subscription", "failureReason": null, "failureCode": null, "redirectUrl": null }
```

**Response 200** (completado)
```json
{ "status": "Completed", "currentStep": null, "failureReason": null, "failureCode": null, "redirectUrl": "https://freedomtax.taxproffice.com" }
```

**Response 200** (falló, en revisión manual)
```json
{ "status": "ManualReview", "currentStep": null, "failureReason": "Downstream unavailable after 3 retries", "failureCode": "Subscription.RequestFailed", "redirectUrl": null }
```

Nunca devuelve `onboardingId`. El frontend debe mostrar un mensaje de "estamos preparando tu cuenta,
te avisaremos por email" y dejar de hacer poll tras un estado terminal (`Completed`,
`ProvisioningFailed`, `ManualReview`, `Cancelled`, `Expired`, `Refunded`) — `ManualReview` puede
tardar horas (interviene un PlatformAdmin), no reintentar el poll indefinidamente en el cliente.

**Errores**: `Onboarding.NoToken`/`Onboarding.InvalidToken` (400/404 si el token no resuelve nada).

---

### 2.10 `GET onboarding/receipts/{fileId}/download`

Redirect 302 a una URL presignada de CloudStorage recién generada. `fileId` viene embebido en el
link del email de recibo — el frontend nunca construye esta URL manualmente, solo la sigue (o el
usuario hace click directo desde su cliente de correo).

**Response**: `302 Found`, header `Location` con la URL presignada real (expira en minutos —
por eso este endpoint existe: el link del email nunca vence porque siempre resuelve una fresca).

**Errores**: `Onboarding.ReceiptFileId` (404 — fileId inválido o el recibo aún no se generó).

---

### 2.11 `GET auth/onboarding/terms/current?kind=TermsOfService&locale=en-US`

**Response 200**
```json
{
  "termsVersionId": "1a2b3c4d-0000-0000-0000-000000000001",
  "kind": "TermsOfService",
  "version": "2026-07-14",
  "contentUri": "https://taxvision.com/legal/tos-2026-07-14.html",
  "contentHash": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b85",
  "locale": "en-US",
  "effectiveFromUtc": "2026-07-14T00:00:00Z",
  "effectiveUntilUtc": null
}
```
`kind` es un enum (`TermsKind`) — valores confirmados en código: consultar
`TaxVision.Auth.Domain.Onboarding.TermsVersions.TermsKind` para el set completo (al menos incluye
`TermsOfService`). El frontend debe mostrar `contentUri` (o el HTML fetcheado desde ahí) y enviar de
vuelta `termsVersionId` en el paso 2.8, sin editorializar el `contentHash` (es integridad, no
display).

---

## 3. Matriz de estados (`TenantOnboardingStatus`)

| Estado | Significado | ¿Terminal? | Transición típica |
|---|---|---|---|
| `PendingPayment` | Onboarding creado (§2.4), esperando que el usuario complete el checkout de Stripe. | No | → `PaymentProcessing` al iniciar checkout, o `Expired` si nunca se completa. |
| `PaymentProcessing` | Checkout Session creada (§2.5), esperando el webhook de Stripe. | No | → `PaymentCompleted` (webhook OK) o `PaymentFailed` (webhook de fallo). |
| `PaymentCompleted` | Pago confirmado, recibo en generación, email de registro enviado. | No | → `RegistrationPending` (transición interna casi inmediata). |
| `RegistrationPending` | Esperando que el usuario haga click en el link del email y complete el form (§2.7-2.8). | No | → `Provisioning` al llamar `register/complete`. |
| `Provisioning` | La Saga está corriendo (§44.3 del README) — `CurrentStep` indica en qué paso va. | No | → `Completed` (éxito) o `ProvisioningFailed` (paso falla). |
| `ProvisioningFailed` | Un paso de la Saga falló. Si es `Transient`, hay retry automático en curso (5min→15min→1h). | No (transitorio, puede volver a `Provisioning`) | → `Provisioning` (retry exitoso) o `ManualReview` (agotó reintentos o error `Permanent`). |
| `ManualReview` | Requiere intervención de un PlatformAdmin (panel §43.2 del README). | Sí (hasta acción admin) | → `Provisioning` (resume/update-and-resume) o `Cancelled`/`Refunded` (cancel-and-refund) o `Completed` (force-complete). |
| `Completed` | Tenant, owner, suscripción y storage provisionados. Login habilitado en el subdominio. | **Sí** | — |
| `PaymentFailed` | El webhook de Stripe reportó un fallo de pago. | **Sí** | El usuario debe reintentar desde cero (nuevo `POST onboarding`). |
| `Cancelled` | Cancelado por acción admin antes de completar. | **Sí** | — |
| `Expired` | El onboarding quedó sin actividad más allá del TTL esperado sin pagar. | **Sí** | — |
| `Refunded` | Cancelado + reembolsado tras completar parcialmente el provisioning. | **Sí** | — |

## 4. Códigos de error

Todos los errores siguen el shape estándar de `BuildingBlocks.Results.Error` serializado —
`{ "code": "Onboarding.XXX", "message": "..." }` — con el HTTP status resuelto por
`ErrorHttpMapping.ToHttpStatusCode()`. Nota honesta: la mayoría de los errores de Onboarding
(incluyendo throttling: `OtpRateLimited`, `ResendCooldown`, `ResendLimitExceeded`) caen en el
**400 default** de ese mapper, no en `429` — el mapper no tiene entradas específicas para el módulo
Onboarding salvo los 3 `NotFound` listados abajo. Frontend: no asumir `429` para throttling de
onboarding, chequear el `code`, no el status HTTP, para decidir el mensaje al usuario.

| Code | HTTP | Origen |
|---|---|---|
| `Onboarding.NotFound` | 404 | Onboarding no existe |
| `Onboarding.ChallengeNotFound` | 404 | Challenge OTP no existe |
| `Onboarding.TokenReferenceNotFound` | 404 | Referencia Redis ya consumida/expirada (TTL 30s) |
| `Onboarding.OtpRateLimited` | 400 | Throttle 5/email/hr o 10/IP/hr |
| `Onboarding.ResendCooldown` | 400 | Menos de 60s desde el último resend |
| `Onboarding.ResendLimitExceeded` | 400 | Más de 5 resends en el challenge |
| `Onboarding.OtpExpired` | 400 | TTL de 10 min vencido |
| `Onboarding.OtpLocked` | 400 | 5 intentos fallidos de verify agotados |
| `Onboarding.OtpMismatch` | 400 | Código incorrecto |
| `Onboarding.ChallengeEmailMismatch` | 400 | El email del challenge no coincide con el del create |
| `Onboarding.EmailNotVerified` | 400 | El challenge referenciado no está verificado |
| `Onboarding.InvalidState` | 400 | Transición de estado no permitida desde el estado actual |
| `Onboarding.SubdomainTaken` | 400 | Slug no disponible |
| `Onboarding.SubdomainReservedTemporarily` | 400 | Otro onboarding (o el flujo de alta directa vía `TenantDomains`) tiene el slug reservado ahora mismo |
| `Onboarding.SubdomainNotReserved` | 400 | El slug del registro no tiene reserva activa de este onboarding |
| `Onboarding.InvalidToken` / `TokenUsed` / `TokenExpired` | 400 | `RegistrationToken` inválido/consumido/vencido |
| `Onboarding.TermsNotAccepted` | 400 | `termsAccepted=false` |
| `Onboarding.TermsVersionNotCurrent` | 400 | Se publicó una versión más nueva de términos entre el preview y el submit |
| `TermsVersion.NotFound` | 404 | `termsVersionId` no existe |

## 5. Invariantes de seguridad

- `OnboardingId` se expone **una sola vez**, en la respuesta de `POST onboarding` (§2.4) — el
  frontend lo retiene en memoria (no `localStorage`, no query string) solo mientras dura esa sesión
  de checkout. Todo lo posterior al pago usa tokens opacos (`RegistrationToken`,
  `statusUrl`/`token` de polling, `fileId` del recibo).
- El password del owner **nunca** viaja por RabbitMQ — se hashea en la primera línea de
  `CompleteOnboardingRegistrationHandler` y solo un `PasswordHashReference` (Redis GETDEL, TTL 30s)
  cruza hacia la Saga.
- El `RegistrationToken` crudo tampoco cruza RabbitMQ — mismo patrón TokenReference.
- Rate limiting real en 6 de los 12 endpoints (§2, columna implícita "Rate limit" del README §44.1);
  los otros 6 dependen de contadores en el propio aggregate (`MaxAttempts`, `MaxResends`) en vez de
  un límite HTTP.

## 6. Verificación E2E manual (checklist previo a merge)

Requiere el stack completo levantado (RabbitMQ + Redis + SQL Server + Auth + PaymentApp + Tenant +
Subscription + Documents + Notification + Scribe, más Stripe en modo test) — **no ejecutado en esta
sesión** (ver README §44.7). El equipo debe correr esto manualmente antes de mergear a producción:

1. `POST onboarding/email-challenges` → confirmar que llega el email real con el código → `verify`
   con el código correcto (204) y luego con uno incorrecto a propósito, repetido 5 veces, para
   confirmar `Onboarding.OtpLocked` en el 6to intento.
2. `POST onboarding` → `POST onboarding/checkout` → seguir `checkoutUrl` en el browser → pagar con
   la tarjeta de test de Stripe `4242 4242 4242 4242` → confirmar que llega el email de recibo con
   el link de descarga (§2.10) y el email de "completa tu registro" con el `RegistrationToken`.
3. Click en el link de registro → `preview` → `terms/current` → `subdomains/check` → `complete` →
   poll de `status` hasta `Completed` → confirmar tenant creado + usuario owner + suscripción activa
   + login funcional en `https://{subdomain}.{TenantBaseDomain}`.
4. Durante el paso 3, matar el proceso de `TaxVision.Subscription.Api` justo antes de que la Saga
   llegue al paso `Subscription` → confirmar que el `status` poll muestra `ProvisioningFailed` con
   `failureCode` empezando en `Subscription.` → levantar Subscription de nuevo → confirmar que el
   retry automático (5min/15min/1h) lo recupera solo, sin intervención manual.
5. Repetir el paso 4 pero dejando el plan borrado (fallo permanente, no transitorio) → confirmar que
   escala a `ManualReview` tras agotar los 3 reintentos → desde el panel admin
   (`auth/onboarding/admin/{id}/cancel-and-refund` con `Confirmation="I understand this is
   irreversible"`) → confirmar el refund real en el dashboard de Stripe test.
6. `grep -ri "passwordPlaintext\|Str0ng-P@ssw0rd" logs/*.json` (o el sink de Serilog configurado) tras
   correr el paso 3 completo → confirmar cero coincidencias — el password nunca debe aparecer en
   texto plano en ningún log.

---

# Billing — API Contract (frontend)

Contrato del flujo de facturación (crear factura → emitirla → cobrarla) para el equipo de frontend.
Todos los endpoints son de `TaxVision.Billing` salvo donde se indica. A diferencia de PayFlow, **todos
los endpoints de Billing requieren `Authorization: Bearer {jwt}`** (usuario autenticado del tenant) —
no hay ningún endpoint anónimo en este servicio.

Base URL: `{{UrlBase}}` (Gateway, prefijo `/billing`). Ej.: `POST {{UrlBase}}/billing/invoices`.

**Nota honesta sobre el estado de implementación** — Billing está parcialmente construido (fase "B1"
del plan interno, `documents/architecture/billing/15_Billing_Implementation_Plan.md`). Lo que sigue
documenta **exactamente lo que existe en código hoy**, no el diseño completo a futuro:
- **Funciona end-to-end**: crear borrador → emitir → generación automática de link de cobro + PDF →
  cobro real → factura marcada pagada → PDF regenerado con sello de pagado.
- **NO implementado todavía** (existen en el enum de estados y/o en los contratos de eventos, pero sin
  código que los dispare): marcar una factura como `Sent` (enviada por email), `Voided` (anulada),
  reembolsos (`Refunded`), y la emisión de un `PaymentReceipt` como documento separado del PDF de
  factura. **No construyan UI para estas acciones — no hay endpoint que las respalde.**
- Los permisos granulares `billing.view`/`billing.manage` (`Authorization/BillingPermissions.cs`) están
  **catalogados pero no wireados** — hoy cualquier usuario autenticado del tenant (`[Authorize]` simple,
  sin política de permiso) puede llamar cualquier endpoint de Billing. No asuman que un usuario sin el
  permiso "billing.manage" recibe 403 — todavía no es así.

## Índice (Billing)

1. [El flujo, paso a paso](#1-el-flujo-paso-a-paso-billing)
2. [Endpoints](#2-endpoints-billing)
3. [Matriz de estados (`InvoiceStatus`)](#3-matriz-de-estados-invoicestatus)
4. [El PDF de la factura — por qué no llega en la respuesta HTTP](#4-el-pdf-de-la-factura--por-qué-no-llega-en-la-respuesta-http)
5. [El link de cobro — cómo paga el cliente](#5-el-link-de-cobro--cómo-paga-el-cliente)
6. [Códigos de error](#6-códigos-de-error-billing)
7. [Invariantes / notas para frontend](#7-invariantes--notas-para-frontend)

## 1. El flujo, paso a paso {#1-el-flujo-paso-a-paso-billing}

```
1. Frontend crea el borrador         →  POST billing/invoices               → invoiceId
2. Frontend emite la factura         →  POST billing/invoices/{id}/issue    → invoiceNumber
   [Async, disparado por el paso 2, el frontend NO espera esto en la misma request]
   2a. Billing → PaymentClient (M2M): asegura un link de cobro estable
   2b. Billing → Documents (M2M): pide generar el PDF (con el link de cobro ya embebido)
   2c. Documents renderiza + sube el PDF a CloudStorage → evento → Billing guarda PdfFileId
3. Frontend hace poll de              GET billing/invoices/{id}
   hasta que `checkoutUrl` y `pdfFileId` dejen de ser null (ver §4-§5, normalmente algunos segundos)
4. Frontend descarga el PDF          →  POST storage/files/{pdfFileId}/download-url  (CloudStorage,
   no Billing — ver §4)
5. Frontend comparte/copia el link de cobro (`checkoutUrl` de §3) — es la misma URL embebida en el PDF
6. [Fuera de Billing] El pagador abre `checkoutUrl`, es redirigido a la página de checkout real, paga
7. [Async] PaymentClient publica el pago → Billing marca la factura `Paid` (o `PartiallyPaid` si el
   monto no cubre el total) → regenera el PDF con sello "Paid" (mismo `pdfFileId`, sobreescrito)
8. Frontend, si necesita registrar un pago que NO pasó por el link (efectivo, cheque, transferencia)  →
   POST billing/invoices/{id}/record-payment  (alternativa manual al paso 6-7)
```

No hay OTP ni verificación de email en este flujo — el usuario ya está autenticado (JWT del tenant)
desde antes de llegar a la pantalla de facturación.

## 2. Endpoints {#2-endpoints-billing}

### 2.1 `POST billing/invoices`

Crea el borrador de la factura (`InvoiceStatus.Draft`). No dispara ningún M2M todavía — eso ocurre
recién al emitir (§2.2).

**Request**
```json
{
  "customer": {
    "customerId": "8c2e1a90-1111-2222-3333-444455556666",
    "name": "Ada Lovelace",
    "email": "ada@example.com",
    "phone": "+18095551234",
    "taxId": "001-1234567-8",
    "billing": {
      "line1": "Calle Principal 123",
      "line2": null,
      "city": "Santo Domingo",
      "state": "DN",
      "zip": "10101",
      "country": "DO"
    }
  },
  "currency": "USD",
  "lines": [
    { "description": "Preparación de declaración 2025", "quantity": 1, "unitAmountCents": 25000, "taxBasisPoints": 0 }
  ],
  "notes": "Gracias por su preferencia.",
  "issuer": null
}
```
`customer.billing` es opcional (`null` si se omite). `issuer` es opcional — si se omite, Billing usa el
`IssuerProfile` del tenant (§2.5); solo hace falta pasarlo para sobreescribir puntualmente esta factura.
`taxBasisPoints` es el impuesto en puntos base de esa línea (100000 = 100%; 0 = sin impuesto).
`unitAmountCents` es el precio unitario en centavos, no dólares.

**Response 200**
```json
{ "invoiceId": "a1b2c3d4-0000-0000-0000-000000000000", "status": "Draft" }
```

**Errores**: validación de campos (montos negativos, moneda inválida — código `Money.*`), `Billing.Invoice.InvalidLines` si `lines` viene vacío.

---

### 2.2 `POST billing/invoices/{invoiceId}/issue`

Emite la factura: asigna `invoiceNumber` correlativo del tenant, fija `issueDateUtc`/`dueDateUtc`, y
**dispara en segundo plano** (fuera de esta misma request/response) la orquestación M2M de §4-§5. Solo
funciona si la factura está en `Draft` — llamarlo dos veces no re-emite (ver `Billing.Invoice.NotDraft`
abajo, no confundir con reintento idempotente: esto SÍ es un error si ya se emitió).

**Request**: sin body.

**Response 200**
```json
{ "invoiceId": "a1b2c3d4-0000-0000-0000-000000000000", "invoiceNumber": "INV-2026-000042", "status": "Issued" }
```

El `checkoutUrl` y el `pdfFileId` **no vienen en esta respuesta** — todavía no existen en el momento
en que esta llamada retorna (ver §1 pasos 2a-2c). El frontend debe hacer poll de §2.4 para obtenerlos.

**Errores**: `Billing.Invoice.NotFound` (404), `Billing.Invoice.NotDraft` (400 — ya fue emitida o está
en otro estado).

---

### 2.3 `GET billing/invoices?take={n}`

Lista las últimas `take` facturas del tenant (orden descendente por fecha de creación).

**Response 200**
```json
[
  {
    "id": "a1b2c3d4-0000-0000-0000-000000000000",
    "invoiceNumber": "INV-2026-000042",
    "status": "Issued",
    "currency": "USD",
    "subtotalCents": 25000, "taxTotalCents": 0, "totalCents": 25000,
    "amountDueCents": 25000, "amountPaidCents": 0,
    "pdfFileId": "b2c3d4e5-0000-0000-0000-000000000000",
    "createdAtUtc": "2026-07-29T10:00:00Z", "paidAtUtc": null,
    "paymentMethod": null, "receiptNumber": null, "receiptHash": null,
    "checkoutUrl": "https://api.taxproffice.com/payments-client/invoices/xk7f2a9b"
  }
]
```
Mismo shape (`InvoiceSummaryResponse`) que §2.4 individual, en array.

---

### 2.4 `GET billing/invoices/{invoiceId}`

**Response 200**: mismo shape `InvoiceSummaryResponse` que un elemento del array de §2.3. Este es el
endpoint que el frontend debe pollear tras §2.2 hasta que `pdfFileId` y `checkoutUrl` dejen de ser
`null` (normalmente unos pocos segundos — ambos M2M son asíncronos vía outbox/Wolverine con reintento
1s/5s/15s si el downstream está caído).

**Campos relevantes para el poll**:
- `checkoutUrl: string | null` — no-null tan pronto Billing↔PaymentClient completa (§5). Suele llegar
  primero porque es el primer paso de la orquestación.
- `pdfFileId: string (uuid) | null` — no-null tan pronto Documents termina de renderizar+subir el PDF
  (§4). **No es una URL** — es un `fileId` de CloudStorage; para descargar hace falta el paso extra
  de §4.
- `status` transiciona de `Issued` a `Paid`/`PartiallyPaid` de forma totalmente independiente del poll
  de estos dos campos — puede que el PDF y el link ya estén listos y la factura siga `Issued`
  (esperando que paguen) por horas o días.

**Errores**: `Billing.Invoice.NotFound` (404).

---

### 2.5 `POST billing/invoices/{invoiceId}/record-payment`

Registra un pago manual (efectivo, cheque, transferencia — cualquier cosa que no pasó por el link de
cobro online). Alternativa al flujo automático de §1 paso 7.

**Request**
```json
{ "method": "Cash", "amountCents": 25000, "paidAtUtc": "2026-07-29T15:30:00Z" }
```
`method` es el string del enum `PaymentMethod`: `"Online" | "Card" | "Cash" | "Check" | "BankTransfer" | "Other"`
(aunque `"Online"` normalmente lo setea el sistema solo, vía §1 paso 7 — un frontend de staff
registrando un pago manual usaría `Cash`/`Check`/`BankTransfer`/`Other`). `amountCents` es opcional —
si se omite, se asume pago total (`Total`). `paidAtUtc` es opcional — si se omite, se usa la hora
actual del servidor.

**Response 200**
```json
{ "invoiceId": "a1b2c3d4-0000-0000-0000-000000000000", "status": "Paid" }
```
`status` puede volver `"PartiallyPaid"` si `amountCents` es menor al total pendiente. Llamarlo de nuevo
sobre una factura ya `Paid` es **idempotente** — no falla, no duplica el pago, simplemente no hace nada
(mismo `status` de vuelta).

**Errores**: `Billing.Invoice.NotFound` (404), `Billing.Invoice.NotPayable` (400 — la factura está en
`Draft` o `Voided`, nunca fue emitida o ya no acepta pagos).

---

### 2.6 `GET billing/issuer-profile`

Perfil de emisor del tenant (nombre, dirección, logo) — se usa como default en §2.1 cuando `issuer`
se omite.

**Response 200**
```json
{
  "name": "FreedomTax LLC", "taxId": "001-9876543-2",
  "line1": "Av. Winston Churchill 500", "city": "Santo Domingo", "state": "DN", "zip": "10147", "country": "DO",
  "phone": "+18095550000", "email": "billing@freedomtax.com", "website": "https://freedomtax.com"
}
```
Todos los campos salvo `name` pueden ser `null` si el tenant nunca configuró su perfil de emisor.

---

### 2.7 `PUT billing/issuer-profile`

**Request**: mismo shape que la respuesta de §2.6 sin `name`... en realidad **sí incluye `name`**
(único campo obligatorio):
```json
{
  "name": "FreedomTax LLC", "taxId": "001-9876543-2",
  "line1": "Av. Winston Churchill 500", "city": "Santo Domingo", "state": "DN", "zip": "10147", "country": "DO",
  "phone": "+18095550000", "email": "billing@freedomtax.com", "website": "https://freedomtax.com"
}
```

**Response**: `204 No Content`.

## 3. Matriz de estados (`InvoiceStatus`) {#3-matriz-de-estados-invoicestatus}

| Estado | Significado | ¿Terminal? | Transición típica |
|---|---|---|---|
| `Draft` | Creada (§2.1), editable, sin número de factura todavía. | No | → `Issued` al llamar §2.2. |
| `Issued` | Emitida (§2.2), esperando pago. `checkoutUrl`/`pdfFileId` se completan async (§1). | No | → `Paid`/`PartiallyPaid` (pago cubre/no cubre el total). |
| `PartiallyPaid` | Recibió un pago que no cubre el total (§2.5 con `amountCents` parcial). | No | → `Paid` (pago adicional que completa el total). |
| `Paid` | Pagada por completo. `paidAtUtc`, `receiptNumber`, `receiptHash` quedan fijos. | **Sí** | — |
| `Sent` | *(en el enum, sin transición implementada — ver nota al inicio del documento)* | — | No construir UI para esto todavía. |
| `Voided` | *(en el enum, sin transición implementada — ver nota al inicio del documento)* | — | No construir UI para esto todavía. |

## 4. El PDF de la factura — por qué no llega en la respuesta HTTP {#4-el-pdf-de-la-factura--por-qué-no-llega-en-la-respuesta-http}

Emitir una factura (§2.2) **no genera el PDF de forma síncrona**. La cadena real es:

```
POST billing/invoices/{id}/issue  (200, sin pdfFileId)
   └─▶ Billing → Documents (M2M, fire-and-forget): "generá este PDF" → Documents responde 202 al toque
         └─▶ Documents renderiza HTML→PDF (Chromium) en segundo plano, sube a CloudStorage
               └─▶ evento "documento listo" → Billing graba pdfFileId en la factura
```

Por eso el frontend debe: (1) pollear `GET billing/invoices/{id}` (§2.4) hasta que `pdfFileId` deje de
ser `null`, y (2) una vez tenga el `pdfFileId`, pedirle la URL de descarga real **a CloudStorage, no a
Billing**:

```
POST storage/files/{pdfFileId}/download-url
Authorization: Bearer {jwt}      ← requiere el mismo JWT del usuario, CloudStorage valida tenant/owner

Response 200:
{ "fileId": "b2c3d4e5-...", "downloadUrl": "https://.../taxvision-storage/...?X-Amz-...", "expiresAtUtc": "2026-07-29T10:05:00Z" }
```

`downloadUrl` es una URL presignada de MinIO/S3 con vencimiento corto (minutos) — el frontend la usa
de inmediato (`window.open(downloadUrl)` o similar), nunca la persiste ni la cachea; si el usuario
vuelve más tarde, hay que volver a llamar este endpoint con el mismo `pdfFileId`.

**Cuando la factura se marca `Paid`** (§1 paso 7), Documents regenera el PDF con sello de "Pagado" —
**sobreescribe el mismo `pdfFileId`** (no hay un `fileId` separado para la versión pagada, a pesar de
que el dominio internamente tiene un campo `PaidPdfFileId` — ese campo existe en el modelo pero **nunca
se setea en código actual**, no lo usen). Esto significa: si el frontend cacheó el `pdfFileId` de antes
de que se pagara, sigue siendo válido — apunta al mismo archivo, que ahora tiene el sello nuevo — pero
conviene volver a pedir `download-url` (la presignada anterior ya habrá vencido de todos modos).

## 5. El link de cobro — cómo paga el cliente {#5-el-link-de-cobro--cómo-paga-el-cliente}

El `checkoutUrl` que aparece en `GET billing/invoices/{id}` (§2.4) tiene esta forma:

```
https://api.taxproffice.com/payments-client/invoices/{reference}
```

Es una **URL estable y pública** (no requiere JWT, no expira) — es la misma URL que Documents embebe
dentro del PDF (botón/QR "Pagar ahora"). El frontend puede compartirla, copiarla, mandarla por
WhatsApp/email — no hace falta ningún endpoint de Billing para "reenviar el link", es la URL final.

Al abrirla (por cualquier persona, sin login), PaymentClient:
1. Resuelve el `reference` internamente y **redirige 302** a `{CheckoutPageBaseUrl}/pay/{checkoutToken}`
   — un token de checkout efímero, generado recién en ese momento (no antes).
2. Esa página `/pay/{token}` es la pantalla de checkout real (frontend de PaymentClient, no de Billing).

El frontend de Billing **nunca construye ni valida `{reference}`/`{checkoutToken}`** — solo muestra el
`checkoutUrl` tal cual viene de §2.4/§2.3, como un link o QR.

Una vez que el pagador completa el pago en esa pantalla, el ciclo se cierra solo (§1 paso 7) — Billing
recibe el evento, marca la factura pagada, y el frontend lo ve reflejado la próxima vez que llame
§2.4/§2.3 (`status: "Paid"`, `paymentMethod: "Online"`).

## 6. Códigos de error {#6-códigos-de-error-billing}

Mismo shape estándar que PayFlow (§4 arriba): `{ "code": "Billing.XXX", "message": "..." }`.

| Code | HTTP | Origen |
|---|---|---|
| `Billing.Invoice.NotFound` | 404 | `invoiceId` no existe (o no pertenece al tenant) |
| `Billing.Invoice.NotDraft` | 400 | Se intentó emitir (§2.2) una factura que no está en `Draft` |
| `Billing.Invoice.NotPayable` | 400 | Se intentó registrar un pago (§2.5) sobre una factura `Draft`/`Voided` |
| `Billing.Invoice.InvalidLines` | 400 | El borrador (§2.1) no trae al menos una línea |
| `Billing.Documents.GenerateFailed` / `Billing.Documents.Unreachable` | — | Internos, M2M a Documents falló — el frontend nunca los ve directo (Wolverine reintenta 1s/5s/15s; si agota reintentos, `pdfFileId` simplemente se queda `null` más tiempo del esperado) |
| `Billing.PaymentClient.EnsureFailed` / `Billing.PaymentClient.TokenFailed` / `Billing.PaymentClient.Unreachable` | — | Internos, M2M a PaymentClient falló — mismo comportamiento: `checkoutUrl` se queda `null` |

Los últimos 4 códigos son de diagnóstico interno (logs/observabilidad) — no forman parte del contrato
HTTP visible al frontend, pero explican **por qué** `pdfFileId`/`checkoutUrl` podrían tardar más de lo
normal en aparecer durante el poll de §2.4. Si el frontend hace poll por más de ~1 minuto sin que
alguno de los dos campos aparezca, es razonable mostrar un mensaje de "está tardando más de lo normal,
reintentá más tarde" en vez de bloquear indefinidamente.

## 7. Invariantes / notas para frontend {#7-invariantes--notas-para-frontend}

- **Ningún endpoint de Billing es anónimo.** A diferencia de PayFlow, no hay flujo público — el usuario
  siempre llega autenticado.
- **Los permisos `billing.view`/`billing.manage` no están enforced todavía** (ver nota al inicio) — el
  frontend puede ocultar botones de "editar"/"emitir" según el rol que ya tenga del JWT por convención
  de producto, pero el backend hoy no los bloquea si alguien llama el endpoint directo.
- **Montos siempre en centavos** en los DTOs de Billing (`*Cents`), nunca en dólares — a diferencia del
  contrato que Billing usa internamente hacia Documents (decimal dólares), que el frontend nunca ve.
- **`invoiceId` no es sensible** como sí lo es `onboardingId` en PayFlow (§5 arriba) — puede vivir en la
  URL (`/invoices/{id}`), no hace falta tratarlo como opaco.
- **No existe todavía** un endpoint para: reenviar la factura por email (`Sent`), anular una factura
  (`Voided`), emitir un recibo de pago como documento separado del PDF de factura, ni un webhook/evento
  cross-servicio (`billing.invoice.issued`, etc. — están definidos como contrato en
  `BuildingBlocks/Messaging/BillingIntegrationEvents/` pero ningún código los publica todavía). Si el
  frontend necesita alguno de estos, es trabajo de backend pendiente, no un endpoint que exista y
  falte documentar.
