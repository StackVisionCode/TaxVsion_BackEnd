# PayFlow — API Contract (frontend)

Contrato completo del flujo de onboarding "pay-first" para el equipo de frontend. Todos los endpoints
son de `TaxVision.Auth` salvo donde se indica. Todos los endpoints listados acá son **anónimos**
(sin `Authorization` header) salvo `POST auth/onboarding/terms/publish`, que es solo para el panel
admin de PlatformAdmin y no forma parte del flujo público.

Base URL: `{{UrlBase}}` (Gateway). Todas las rutas están relativas a esa base, sin prefijo `/api`.

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
nombre "check"). Se llama en la pantalla de registro (paso 8), no en el checkout — el `onboardingId`
en este punto viene del `token` decodificado en 2.7 preview, no hace falta que el frontend lo haya
retenido desde 2.4.

**Request**
```json
{ "slug": "freedomtax", "onboardingId": "8c2e1a90-1111-2222-3333-444455556666", "email": "buyer@example.com" }
```

**Response 200**
```json
{ "available": true, "reason": null, "expiresAtUtc": "2026-07-29T11:15:00Z" }
```
o si no está disponible:
```json
{ "available": false, "reason": "TAKEN", "expiresAtUtc": null }
```

Llamar de nuevo con el mismo `onboardingId` + `slug` renueva la reserva (idempotente) — útil si el
usuario tarda en completar el form.

**Errores**: `Onboarding.SubdomainTaken` (400), formato inválido de slug (400, código de
`SubdomainSlug` VO), `Onboarding.SubdomainReservationEmail` (400 — el email no coincide con el de la
reserva activa de otro slug).

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
{ "status": "Completed", "currentStep": null, "failureReason": null, "failureCode": null, "redirectUrl": "https://freedomtax.taxprocore.com" }
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
