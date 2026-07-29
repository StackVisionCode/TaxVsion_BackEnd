# ADR-001 — Auth aloja el módulo Onboarding

Estado: **APPROVED**
Fecha: 2026-07-29

## ID y contexto

**ID:** PFDR-001. PayFlow necesita un bounded context para el flujo "pago-primero": un comprador
anónimo verifica su email, paga en Stripe Checkout, y solo **después** se provisiona un tenant real
(Fase 3 en adelante del plan maestro, `PayFlow_Implementation_Plan.md`). Ese contexto no encaja
limpiamente en ningún servicio existente: no es un tenant (todavía no existe uno), no es facturación
recurrente (eso es `Subscription`), y no es solo checkout (eso es `PaymentApp`). Había que decidir
dónde vivir: un microservicio nuevo (`TaxVision.Onboarding`) o un módulo dentro de un servicio
existente.

## Evidencia real

- El resultado final del flujo **es** un `User` con `ActorType.TenantAdmin` — el mismo aggregate que
  Auth ya posee y ya sabe crear (`User.Register`, usado por `AcceptInvitationHandler` en el flujo de
  invitación tradicional).
- Auth ya posee `TenantRegistry`, `IRoleRepository`, `IAuthSessionIssuer` — todo lo que la Saga
  necesita para el paso `TenantAdmin` (Fase 15) sin llamar a otro servicio.
- Auth ya posee el mecanismo de sesiones/JWT que el usuario final va a usar para loguearse apenas
  termine el provisioning — no hay que replicarlo en un servicio nuevo.
- `TermsVersion`/`TenantTermsAcceptance` (Fase 6) son, por naturaleza, aceptación de contrato ligada
  a un usuario — Auth ya tenía `TenantTermsAcceptance` desde antes de PayFlow (se retrofitteó, no se
  creó desde cero).
- Un microservicio `TaxVision.Onboarding` nuevo hubiera necesitado: su propio JWT/sesión para el
  "usuario en proceso de registrarse" (que no es un `User` real todavía), su propia infraestructura
  de EF Core/RabbitMQ/Redis (duplicando lo que Auth ya tiene), y — el punto que más pesa — la Saga
  de Fase 15 llama al paso `TenantAdmin` como una llamada M2M **loopback**; si viviera en otro
  servicio, ese paso cruzaría la red dos veces (Onboarding→Auth→Onboarding) para lo que hoy es una
  invocación in-process.

Clasificación: **VERIFIED** — el código de las Fases 3-18 ya implementa esta decisión; este ADR
documenta el motivo a posteriori (deuda de documentación que Fase 19 cierra), no una propuesta.

## Alternativas

1. **Microservicio nuevo `TaxVision.Onboarding`**, dueño de `TenantOnboarding`,
   `EmailVerificationChallenge`, `TermsVersion`, con su propia Saga llamando a Auth/Tenant/
   Subscription vía M2M para cada paso, incluido el paso `TenantAdmin`.
2. **Módulo dentro de Auth** (`TaxVision.Auth.Application.Onboarding` /
   `TaxVision.Auth.Domain.Onboarding`), con namespace propio y una fitness function
   (`OnboardingModuleArchitectureTests`) que impide que el resto de Auth dependa de sus internals,
   pero desplegado en el mismo proceso/DB que Auth.
3. **Repartir el estado**: `TenantOnboarding` en Auth, pero `EmailVerificationChallenge`/
   `TermsVersion` en un servicio de "Identity" separado.

## Opción seleccionada y motivo

Opción 2. El comprador **es**, conceptualmente, un usuario de Auth en un estado previo a tener
tenant — no una entidad de un dominio distinto. Modelarlo como módulo (no como servicio) evita
duplicar toda la infraestructura de sesión/JWT/rol que Auth ya posee, y hace que el paso más sensible
de la Saga (`TenantAdmin`, que canjea un hash de password de un solo uso) sea una llamada
**in-process**, no una llamada de red — menos superficie para que el `PasswordHashReference` de un
solo uso se pierda o quede huérfano por un timeout de red.

La opción 3 (repartir el estado) se descartó rápido: `EmailVerificationChallenge` y `TermsVersion` no
tienen ninguna razón de negocio para vivir en otro lugar que no sea junto al `TenantOnboarding` que
los referencia — hubiera sido partición por capricho, no por bounded context real.

## Consecuencias

Positivas:

- Cero llamadas M2M nuevas para el paso `TenantAdmin` de la Saga — es la única invocación
  verdaderamente in-process de las 6 que orquesta `TenantOnboardingProcessManager` (§44.3 del
  README).
- Reuso directo de `IAuthSessionIssuer`, `ITenantRegistry`, `IRoleRepository`, sin duplicar
  contratos ni DTOs de traducción entre servicios.
- Un solo `AuthDbContext`, una sola migración de EF Core por cambio — sin coordinación de esquema
  entre dos bases de datos para lo que es, en esencia, el mismo bounded context de identidad.
- La fitness function (`OnboardingModuleArchitectureTests.NonOnboarding_Files_DoNotReferenceOnboardingInternals`,
  Fase 7 del plan) preserva el aislamiento lógico sin pagar el costo de un despliegue separado —
  cuando Fase 18 necesitó reusar `ITokenReferenceStore` desde el módulo `Invitations` (no
  `Onboarding`), la fitness function lo agarró en el primer build y forzó mover la abstracción a un
  namespace verdaderamente compartido en vez de romper el aislamiento con una excepción ad-hoc.

Negativas:

- Auth crece en superficie (controllers, aggregates, tablas) — el `AuthDbContext` ahora tiene
  `TenantOnboarding`, `EmailVerificationChallenge`, `TermsVersion`, `OnboardingSubdomainReservation`
  además de todo lo que ya tenía.
- El blast radius de un incidente en Auth ahora incluye el flujo de onboarding — un bug en, por
  ejemplo, `SessionsController` podría en teoría afectar el pipeline de deploy de todo el módulo
  Onboarding también (mismo binario, mismo proceso).
- Si en el futuro el volumen de onboarding justifica escalar independiente de Auth (picos de
  campaña de marketing vs. tráfico normal de login), separar el módulo a un servicio propio
  requeriría extraer 4 aggregates + reemplazar la llamada in-process del paso `TenantAdmin` por una
  M2M real — trabajo no trivial, pero acotado porque el módulo ya está aislado por namespace y
  fitness function.

## Riesgos y mitigaciones

| Riesgo | Mitigación |
|---|---|
| Acoplamiento oculto entre Onboarding y el resto de Auth | `OnboardingModuleArchitectureTests` (NetArchTest) prohibe que archivos fuera de `Application/Onboarding`/`Domain/Onboarding` referencien esos namespaces (excepción documentada: `Terms`, que Auth ya poseía antes de PayFlow). |
| Extracción futura costosa si el volumen lo justifica | Namespace y aggregates ya separados; el único punto de fricción real sería reemplazar la llamada in-process del paso `TenantAdmin` por M2M — extracción medible, no reescritura. |
| Blast radius compartido con Auth | Mismo nivel de riesgo que cualquier otro módulo de Auth (RBAC, Sessions, Invitations) — no es un caso especial de PayFlow. |

## Criterios de aceptación

- `OnboardingModuleArchitectureTests` verde en cada build (Fase 7 del plan, ya en el pipeline de
  `TaxVision.Auth.Tests`).
- Cero repositorios/DbContexts compartidos entre el módulo Onboarding y otro servicio — todo el
  acceso a datos de Onboarding pasa por sus propios repos EF Core dentro de `AuthDbContext`.
- El paso `TenantAdmin` de la Saga sigue siendo in-process (`auth/internal/tenants/{id}/owners` es un
  loopback HTTP a sí mismo con token de servicio de vida corta, no una llamada a otro binario).

## Archivos afectados

`src/Services/Auth/Application/Onboarding/`, `src/Services/Auth/Domain/Onboarding/`,
`src/Services/Auth/Api/Controllers/Onboarding*Controller.cs`,
`deploy/tests/TaxVision.Auth.Tests/Architecture/OnboardingModuleArchitectureTests.cs`.

## Estado

**APPROVED**. Decisión ya implementada en Fases 3-18; este ADR es el registro formal requerido por
Fase 19 del plan maestro.
