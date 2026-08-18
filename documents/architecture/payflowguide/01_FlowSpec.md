# PayFlow — 01. Flow Spec

Fuente: `Implementaciones/PayFlowNew/Tenant_Onboarding_Flujo_Seguro_Arquitectura.pdf` (leído completo, 32 páginas) + `Implementaciones/PayFlowNew/flowpay.png` (diagrama de alto nivel, consistente con el texto del PDF — 5 bloques: Plan & Verify, Payment, Registration Invite, Final Registration, Provisioning/Saga). Servicio dueño asignado según §5 de `PayFlow_Implementation_Plan.md`.

---

## a. Los 40 pasos

| # | Nombre (PDF) | Actor | Servicio dueño |
|---|---|---|---|
| 1 | Selección del plan | Humano (usuario) | Subscription (catálogo `GET /plans` existente) → Auth.Onboarding recibe `PlanId` |
| 2 | Introducir email | Humano | Auth.Onboarding |
| 3 | Crear desafío OTP (`EmailVerificationChallenge`) | Sistema | Auth.Onboarding |
| 4 | Verificar OTP | Humano + Sistema | Auth.Onboarding |
| 5 | Solicitar datos iniciales (FirstName/LastName/Phone) | Humano | Auth.Onboarding |
| 6 | Crear `TenantOnboarding` | Sistema | Auth.Onboarding |
| 7 | Primera Unidad de Trabajo (UoW #1) | Sistema | Auth.Onboarding |
| 8 | Crear Checkout | Sistema | PaymentApp (vía M2M desde Auth.Onboarding) |
| 9 | Marcar checkout creado (`PaymentProcessing`) | Sistema | Auth.Onboarding (actualiza su propio agregado tras confirmación de PaymentApp) |
| 10 | Usuario realiza el pago | Humano | Externo (Stripe) — no hay dueño en el monorepo |
| 11 | Webhook `PaymentSucceeded` | Sistema | PaymentApp (Inbox `WebhookEvents`) |
| 12 | UoW crítica del pago | Sistema | PaymentApp (UoW #2) → publica `OnboardingPaymentSucceeded` → Auth.Onboarding consume y marca `PaymentCompleted` (ver hueco §f.2) |
| 13 | "A partir de aquí el cliente YA PAGÓ" (regla de negocio, no paso ejecutable) | — | Auth.Onboarding + PaymentApp (regla transversal) |
| 14 | Generar `RegistrationToken` | Sistema | Auth.Onboarding (UoW #3) |
| 15 | Fallo enviando el email (regla de resiliencia, no paso ejecutable) | — | Notification + Postmaster (Outbox + retry) |
| 16 | Email de registro | Sistema | Scribe (render) + Notification (orquesta) + Postmaster (entrega) |
| 17 | Resolver `RegistrationToken` (`CompleteTenantOnboarding`) | Sistema | Auth.Onboarding |
| 18 | Mostrar formulario final (datos seguros, sin IDs internos) | Sistema | Auth.Onboarding |
| 19 | Validar subdominio | Sistema | Auth.Onboarding (`OnboardingSubdomainReservation`) |
| 20 | Términos y condiciones | Humano + Sistema | Auth.Onboarding (`TermsVersion` + `TenantTermsAcceptance` retrofit) |
| 21 | Request final (password + officeName + subdomain + terms) | Humano | Auth.Onboarding |
| 22 | Validaciones antes del provisioning | Sistema | Auth.Onboarding |
| 23 | Cambio a `Provisioning` (UoW #4) | Sistema | Auth.Onboarding |
| 24 | "Aquí NO una transacción SQL gigante" (principio, no paso ejecutable) | — | Auth.Onboarding (Saga) |
| 25 | Provisioning Paso 1: Crear Tenant (UoW #5) | Sistema | Tenant |
| 26 | Recibir `TenantCreated` | Sistema | Auth.Onboarding (Process Manager) |
| 27 | Provisioning Paso 2: Crear TenantAdmin (UoW #6) | Sistema | Auth (User aggregate, mismo servicio que hostea la Saga) |
| 28 | Password nunca por RabbitMQ (regla de seguridad, no paso ejecutable) | — | Auth |
| 29 | Provisioning Paso 3: Subscription (UoW #7) | Sistema | Subscription |
| 30 | ¿Qué pasa si Subscription falla? (regla de manejo de fallos) | — | Auth.Onboarding (Saga) + Subscription |
| 31 | Idempotencia en TODOS los pasos (principio transversal) | — | Todos los servicios de provisioning (Tenant, Auth, Subscription, CloudStorage) |
| 32 | Cloud/subdomain/recursos adicionales | Sistema | CloudStorage (storage) + Auth.Onboarding vía Tenant/TenantDomain (subdomain) + Auth (defaults: roles/permisos) |
| 33 | ¿Cuándo marcamos `Completed`? (UoW #8) | Sistema | Auth.Onboarding |
| 34 | Token se consume AL FINAL | Sistema | Auth.Onboarding |
| 35 | Si el provisioning tarda (202 Accepted + polling) | Sistema | Auth.Onboarding (`OnboardingStatusController`) |
| 36 | Si falla después del pago (UX de mensaje, no "pay again") | Sistema | Auth.Onboarding (API) + Frontend (consumidor) |
| 37 | Estados recomendados (`TenantOnboardingStatus` + `TenantProvisioningStep`) | — | Auth.Onboarding (Domain) |
| 38 | Reintentos (transient vs permanent) | Sistema | Auth.Onboarding (`FailureClassifier`, Saga) + todos los servicios de provisioning |
| 39 | Compensaciones | Sistema + Humano | Auth.Onboarding (`OnboardingAdminController`) + Subscription (cancel) + PaymentApp (refund) + Tenant (close) |
| 40 | Flujo consolidado y regla arquitectónica (meta-resumen, no un paso ejecutable) | — | Cross-cutting — resumen de las 8 UoW + Saga |

---

## b. Tabla de los 12 estados (`TenantOnboardingStatus`)

| Estado | Significado | Transiciones válidas hacia |
|---|---|---|
| `PendingPayment` | Onboarding persistido en UoW #1, aún no hay pago. | `PaymentProcessing`, `Cancelled`, `Expired` |
| `PaymentProcessing` | Checkout creado en Stripe. Usuario en sesión de pago. | `PaymentCompleted`, `PaymentFailed`, `Cancelled`, `Expired` |
| `PaymentCompleted` | Webhook `payment_succeeded` procesado (UoW #2). Cliente pagó. | `RegistrationPending` |
| `RegistrationPending` | `RegistrationToken` generado y persistido (UoW #3). Email en outbox. | `Provisioning`, `Expired` (72h sin uso), `Refunded` (manual) |
| `Provisioning` | Formulario final validado. Saga arrancada (UoW #4). | `Completed`, `ProvisioningFailed`, `ManualReview` |
| `ProvisioningFailed` | Un paso de la Saga falló. `FailedStep + FailureCode + FailureReason`. | `Provisioning` (retry), `ManualReview` (retry agotado), `Refunded` (manual) |
| `ManualReview` | Requiere intervención humana. | `Provisioning` (resume), `Refunded` (cancel), `Completed` (force-complete excepcional) |
| `Completed` | Todos los pasos obligatorios finalizaron. Token consumido. Tenant operativo. | (final) |
| `PaymentFailed` | Proveedor de pago rechazó el cobro. | `Cancelled` (manual) |
| `Cancelled` | Onboarding cancelado antes de completar. | (final) |
| `Expired` | TTL agotado (nunca pagó, o token no usado en 72h). | (final) |
| `Refunded` | Refund emitido a Stripe + compensaciones ejecutadas. | (final) |

---

## c. Tabla de los 8 pasos de provisioning (`TenantProvisioningStep`)

| Step | Cuándo se setea |
|---|---|
| `None` | Antes de arrancar la Saga |
| `Tenant` | Comando `CreateTenantForOnboarding` en vuelo |
| `TenantAdmin` | Tras `TenantCreatedForOnboarding`, comando `CreateTenantOwner` en vuelo |
| `Subscription` | Tras `TenantOwnerCreated`, comando `ActivateSubscription` en vuelo |
| `CloudStorage` | Tras `SubscriptionActivated`, comando `ProvisionStorage` en vuelo |
| `Subdomain` | Tras `StorageProvisioned`, comando `ActivateSubdomain` en vuelo |
| `Defaults` | Tras `SubdomainActivated`, comando `ConfigureDefaults` en vuelo |
| `Completed` | Tras `DefaultsConfigured`, UoW #8 final marcó `Status=Completed` |

`FailedStep` guarda el step exacto donde ocurrió el fallo cuando `Status=ProvisioningFailed`.

---

## d. Tabla de las 8 Units of Work

| UoW | Servicio | Responsabilidad | Resultado persistente |
|---|---|---|---|
| #1 | Auth | Crear `TenantOnboarding` | `PendingPayment` + comprador + `PlanId`, ANTES del checkout |
| #2 | PaymentApp | Confirmar pago del webhook | Inbox `WebhookEvents` + `SaaSPayment.Status=Succeeded` + Outbox `OnboardingPaymentSucceeded` |
| #3 | Auth | Preparar registro | `RegistrationTokenHash + ExpiresAt` + `Status=RegistrationPending` + Outbox `OnboardingRegistrationReady` |
| #4 | Auth | Iniciar Saga | `Status=Provisioning` + `OfficeName + RequestedSubdomain + TermsVersionId + ContentHash + IP + UA` |
| #5 | Tenant | Crear Tenant | Persist idempotente por `OnboardingId` + Outbox `TenantCreatedForOnboarding` |
| #6 | Auth | Crear TenantAdmin User | Persist User (password hasheado) + Outbox `TenantOwnerCreated` |
| #7 | Subscription | Activar suscripción | Persist `TenantSubscription` en `Active` + Outbox `SubscriptionActivatedForOnboarding` |
| #8 | Auth | Finalizar Onboarding | `TenantId + UserId + SubscriptionId + RegistrationTokenUsedAt=NOW + Status=Completed` + Outbox `TenantOnboardingCompleted` |

Los pasos intermedios de la Saga (`CloudStorage`, `Subdomain`, `Defaults`) siguen el mismo patrón — UoW local en su servicio + Outbox del evento resultado — pero no tienen un número de UoW propio en la tabla de 8 porque el PDF los agrupa conceptualmente dentro del bloque "Provisioning (Saga)"; en la práctica cada uno es una transacción local adicional coordinada por el mismo Process Manager.

---

## e. Matriz de fallos (Anexo C del PDF) + clasificación transient/permanent

| Punto | Fallo | Acción | Garantía | Clasificación |
|---|---|---|---|---|
| Antes de crear onboarding | DB no disponible | Detener | No crear checkout; no hay obligación de pago. | Transient |
| Checkout creado | Falla persistencia local | Recuperar | Reconciliar por `CheckoutReference` + `OnboardingId`; no continuar silenciosamente. | Transient |
| Webhook duplicado | Evento repetido | Ignorar seguro | Inbox / `ProviderEventId` evita doble procesamiento. | N/A (no es una falla, es el mecanismo de idempotencia funcionando) |
| Después de `PaymentCompleted` | Email/Notification caído | Retry | Outbox conserva el mensaje; el cliente no vuelve a pagar. | Transient |
| Provisioning Tenant | Tenant Service falla | Retry / Stop | No crear usuario ni subscription hasta `TenantCreated`. | Transient (DB/red) o Permanent (`SubdomainConflict`) según causa — ver `FailureClassifier` §7.1 del plan |
| Provisioning Subscription | Subscription falla | `ProvisioningFailed` | Retry automático; luego `ManualReview` si persiste. | Transient (DB/red) o Permanent (`Plan.NotFound`/`Plan.Deactivated`) según causa |
| Infra externa | Cloudflare/Storage timeout | Retry | Fallo transitorio con backoff; mantener paso actual. | Transient |
| Error permanente | Plan/configuración inconsistente | `ManualReview` | No retry infinito; soporte decide corrección o compensación. | Permanent |
| Fallo no recuperable | No puede completarse el servicio | Compensar | Deshabilitar tenant, cancelar recursos y/o refund según reglas comerciales. | Permanent |

La clasificación exacta por código de error (`Tenant.DbUnavailable`, `Tenant.SubdomainConflict`, `User.EmailConflict`, `Plan.NotFound`, `Terms.VersionInactive`, `Payment.Refunded`, `Config.PlatformIssuerMissing`, `Onboarding.InvalidState`, etc.) ya está resuelta en detalle en `PayFlow_Implementation_Plan.md` §7.1 (`FailureClassifier`) — esta tabla es la vista de alto nivel del PDF, aquella es la vista operativa completa.

---

## f. Huecos no especificados por el PDF

1. **Selección del plan (paso 1)**: Flujo nuevo no especifica este punto — recomendación: reusar `GET /plans` existente de Subscription (`PlansController.cs`, ya implementado) y validar `Plan.IsActive` en el momento de crear el Checkout (no solo en el frontend), para rechazar onboarding con planes desactivados entre la selección y el pago.

2. **Frontera de servicio del "UoW crítica del pago" (pasos 11-13)**: el PDF describe una sola transacción conceptual que registra el webhook, valida el `TenantOnboarding`, y marca `Status=PaymentCompleted` — pero PaymentApp y Auth son servicios distintos con bases de datos distintas, así que esto **no puede ser una sola transacción**. El propio plan ya resuelve esto correctamente separando en UoW #2 (PaymentApp: confirma su propio `SaaSPayment` + publica `OnboardingPaymentSucceeded`) y UoW #3 (Auth: consume el evento y avanza `TenantOnboarding`) — pero el PDF no aclara si la transición intermedia `PaymentCompleted` (antes de `RegistrationPending`) debe persistirse como su propio commit separado dentro del consumer de Auth, o si puede colapsarse en un solo commit `PaymentCompleted → RegistrationPending`. Recomendación: colapsar en un solo commit dentro de `OnboardingPaymentSucceededConsumer` (nada observable ocurre entre esos dos estados intermedios), documentando explícitamente ambos valores de `Status` en el histórico de eventos de dominio del aggregate para no perder trazabilidad.

3. **Validar subdominio (paso 19) — concurrencia**: el PDF no dice si la reserva de subdominio es un "soft hold" que bloquea a otros onboardings concurrentes entre el paso 19 (validación) y el paso 25 (creación real del Tenant), o si es solo una validación puntual en el momento del submit. Flujo nuevo no especifica este punto — recomendación: usar `OnboardingSubdomainReservation` (Fase 14 del plan) con TTL 60 min como soft-lock, para que dos onboardings concurrentes no reclamen el mismo subdominio entre la validación y la creación efectiva del `Tenant`.

4. **Si el provisioning tarda (paso 35) — cadencia de polling**: el PDF menciona `202 Accepted` + `GET /registration/status` pero no especifica cadencia de polling ni timeout de UI. Flujo nuevo no especifica este punto — recomendación: polling cada 2s con backoff hasta 30s máximo entre intentos, y timeout total de UI de 2 minutos antes de mostrar "seguimos trabajando, te avisaremos por email" en vez de seguir bloqueando la pantalla indefinidamente.

5. **Abandono del checkout sin pagar y sin webhook de fallo**: el PDF cubre `PaymentSucceeded` y menciona `PaymentFailed` (rechazo explícito del proveedor), pero no cubre el caso donde el usuario simplemente cierra la pestaña de Stripe sin completar el pago — no llega ni éxito ni fallo, el onboarding queda indefinidamente en `PendingPayment`/`PaymentProcessing`. Flujo nuevo no especifica este punto — recomendación: job periódico (`OnboardingExpirationJob`) que expira a `Status=Expired` cualquier `TenantOnboarding` en `PendingPayment` o `PaymentProcessing` sin webhook tras 24h, liberando el `RequestedSubdomain` si hubiera soft-lock activo.

6. **Límite de reintentos de "Resend registration email" (pasos 15-16)**: el PDF menciona la posibilidad de reenviar el email de registro pero no especifica límites. Flujo nuevo no especifica este punto — recomendación: aplicar el mismo patrón de rate-limiting ya usado en `EmailVerificationChallenge.ResendCount` (OTP) al reenvío del email de registro, para evitar abuso.

7. **Regeneración de `RegistrationToken` expirado sin re-pago**: el PDF documenta el TTL de 72h del token pero no especifica qué pasa si expira sin que el usuario complete el registro — ¿debe re-pagar, o solo re-generar el token? Dado que `PaymentCompleted` es irreversible por regla de negocio (§13 del PDF, §3.7 del plan), el cliente **no puede** tener que re-pagar. Flujo nuevo no especifica este punto — recomendación: nuevo endpoint `POST /onboarding/register/resend-token` que verifica `Status IN (RegistrationPending, Expired)` + `PaymentCompletedAt IS NOT NULL`, genera un `RegistrationToken` nuevo invalidando el anterior (nuevo hash, nueva expiración), y transiciona `Expired → RegistrationPending` si aplica — sin tocar el pago en absoluto.

---

## Referencias

- PDF fuente: `Implementaciones/PayFlowNew/Tenant_Onboarding_Flujo_Seguro_Arquitectura.pdf` (32 páginas, leído completo para este documento).
- PNG fuente: `Implementaciones/PayFlowNew/flowpay.png` (diagrama de alto nivel, consistente con el texto — confirma los 5 bloques, los 12 estados con color-coding, y la leyenda de actores).
- Decisiones de bounded context y servicio dueño: `PayFlow_Implementation_Plan.md` §3, §5, §6, §7.
- Baseline del monorepo antes de esta fase: `00_Baseline.md`.
