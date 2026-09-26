# Plan — Flexibilización del Rate Limit y corrección del Load Shedding

> Backlog "Correcciones y Mejoras CRM", punto **4.1 Revisar límites demasiado estrictos**.
> Aprobado el 2026-09-25. Alcance: **F1–F7 + F9**. F8 (reducir volumen de requests) queda como mejora aparte.
> Complementa a `Plan_Implementacion_Fases.md` (plan original de 9 fases, cerrado) y a `ADR_017_RateLimit_Layers.md`.

## 1. Situación

En producción, con sesión iniciada y navegando entre módulos, el usuario queda bloqueado y ve **"Fleet is overloaded. Retry after 5 seconds."** Ese mensaje no viene del rate limit: es el 503 del *load shedder* del Gateway. Además hay límites del rate limit por niveles que bloquean el uso normal del CRM.

El análisis del código (docs, BuildingBlocks, Gateway, Subscription, 24 servicios .NET, Communication en Node y los dos frontends) encontró **cuatro causas que se suman**:

| # | Causa | Evidencia |
|---|---|---|
| 1 | **Load shedding con falsos positivos.** Las conexiones WebSocket del chat se cuentan como requests con toda su vida como "latencia". La exclusión existe pero nunca se aplica, porque `LoadSheddingMiddleware` corre antes de `UseWebSockets()` y `IsWebSocketRequest` siempre da `false`. El "p99" es en realidad el máximo con menos de 100 muestras, se activa con 20 muestras y no tiene histéresis. | `Gateway/Program.cs:115` vs `:133`; `LoadSheddingMiddleware.cs:35`; `RequestOutcomeWindow.cs:85`; `appsettings.json` LoadShedding |
| 2 | **Listados principales a 20 req/min por usuario** (categoría H). Grid de clientes con type-ahead, board, búsqueda y calendario de tareas, calendario, reminders, usuarios, búsqueda de notas. En starter no escala (×1). La ventana deslizante cuenta los rechazos y `Retry-After` = ventana completa: si el front reintenta, nunca sale del bloqueo. | `RateLimitPolicyCatalog.Customer.cs:41-50`; `RedisRateLimitAlgorithmCounter.cs:36-44`; `TieredRateLimitEvaluator.cs:61,118,152` |
| 3 | **Límites por IP compartidos por toda la oficina (NAT).** `/auth/refresh` a 10/min por IP (un 429 ahí desloguea). `GET /auth/invitations` cae en el límite pre-login. Communication REST a 300/min por IP. 20 logins fallidos en 15 min desde una IP bloquean a todos. | `GatewayRateLimitOptions.cs:22-39`; `build-server.ts:64-77`; `LoginThrottler.cs:36-42` |
| 4 | **El front no maneja 429/503.** Muestra el texto crudo del backend (`toApiError(err).message`, 239 usos), no lee `Retry-After`, un refresh fallido por 429/503 desloguea, los reintentos ignoran el status y los rate-limits del socket fallan en silencio. | `error.interceptor.ts:42-48`; `api-error.model.ts:48-51`; `chat.store.ts:310-316` |

Además: **8 formatos de rechazo distintos** (429 vacío, 400, 401, 202, 200…) y **2 bugs que pierden datos**:

- PaymentApp responde **200** a Stripe/PayPal cuando descarta un webhook por throttling, así que nunca lo reintentan (`ProcessProviderWebhookHandler.cs:203-214`).
- Postmaster marca como **fallido permanente** cualquier email por encima de 60/min por tenant (`NotificationsEmailSendRequestedConsumer.cs:296-336`).

## 2. Principios

1. **Por costo y riesgo, no un límite global.** Lecturas y búsquedas: generosas y con ráfaga (token bucket). Escrituras: moderadas. Uploads y llamadas a proveedores externos: según su costo. Seguridad (OTP, reset, MFA, lockout de cuenta, dinero, revelar SSN): siguen estrictas.
2. **Partición por identidad.** Usuario autenticado → tenant+usuario. La IP solo para tráfico anónimo, y dimensionada para oficinas detrás de NAT.
3. **No contar los rechazos** y **`Retry-After` real** (cuándo se libera de verdad un cupo).
4. **Un solo contrato de rechazo.** 429 `RateLimit.Exceeded` y 503 `LoadShedding.Active`, ambos con `retryAfterSeconds` en el body, mensaje profesional en inglés y headers expuestos por CORS.
5. **Ajuste fino en prod sin redeploy** mediante los multiplicadores por plan de la tabla `PlanRateLimits`.

## 3. Cuotas aprobadas (starter; pro/enterprise escalan con su multiplicador)

| Área | Hoy | Nuevo |
|---|---|---|
| Listados/búsqueda (H: clientes, tareas, calendario, reminders, usuarios, notas) | 20/min usuario, 100 tenant, ventana deslizante, ×1/3/15 | **60/min usuario, 600 tenant, token bucket**; multiplicadores H **2/5/10** → starter 120/1200 |
| Uploads (`cloudstorage.i.upload`) | 25/10 min, initiate + complete cuentan doble; +30/min por tenant en el Gateway | **60/10 min usuario, 240 tenant**, solo cuenta el initiate; se quita el límite duplicado del Gateway |
| Abrir emails (Connectors body) | 10/min por buzón | **60/min por buzón** |
| Adjuntos de email (Connectors) | 5/min por tenant | **30/min por buzón** |
| Descarga ZIP | 5/min | **10/min** |
| Disponibilidad de calendario | 10/min | **60/min** |
| Acciones masivas (clientes) | 12/h | **30/h** |
| Revelar SSN/ITIN (N) | 5/h | **20/h**, sigue auditado y sin escalar por plan |
| Login por IP (Gateway) | 10/min por IP+ruta | **30/min**; la protección fuerte sigue por email/cuenta en Auth |
| `/auth/refresh` | 10/min por IP (429 = logout) | Fuera del límite por IP; límite por sesión en Auth |
| `GET /auth/invitations` | 10/min por IP (por error) | Solo el accept público queda en pre-auth; la lista usa su política F normal |
| Communication REST | 300/min por IP | **600/min por usuario autenticado**; 1000/min por IP anónima |
| Tenant lookup (`by-host`) | 30/min por IP | **120/min por IP** |
| Fallos de login por IP | 20/15 min | **50/15 min** (el lockout por cuenta se mantiene) |

Sin cambios (seguridad): OTP por email/challenge, reset de contraseña, intentos de MFA, lockout por cuenta, M (dinero) 5/min, L (cobros), tokens públicos de share/sign, webhooks con firma.

## 4. Fases

### F1 — Load shedding sin falsos positivos (Gateway)
- Detección de upgrade independiente del orden del pipeline (header `Upgrade: websocket` / extended CONNECT), más prefijos *pass-through* configurables (por defecto `/communication/socket.io`, que también cubre el long-polling).
- Medir la latencia del servidor (hasta que arranca la respuesta), no el tiempo de transferencia del cliente. Requests con body grande no se miden.
- `MinSamples` 20→200, `P99LatencyThresholdMs` 2000→5000, activación sostenida (`ActivationSeconds`) e histéresis de recuperación (`RecoveryRatio`).
- Tráfico anónimo agrupado por IP, no como un único tenant `anon`.
- 503 con `retryAfterSeconds` y mensaje profesional.
- Tests: pipeline real de ASP.NET (TestServer) con la exclusión antes de `UseWebSockets`, más señal sostenida e histéresis.

### F2 — Contrato único de rechazo
- 429 `{code:"RateLimit.Exceeded", message, retryAfterSeconds, policy, layer}` y 503 `{code:"LoadShedding.Active", message, retryAfterSeconds}`.
- `OnRejected` compartido en BuildingBlocks para el Gateway y los limitadores nativos (Auth, CloudStorage, Signature, Connectors, PaymentApp, PaymentClient).
- Node alineado (body + `Retry-After` restante + `return reply`).
- CORS del Gateway expone `Retry-After` y `X-RateLimit-*`.
- Los throttles de cara al usuario que devolvían 400/401 pasan a 429 (OTP de onboarding, cooldown/PIN de Signature, accept de invitación). El 202 silencioso de password-forgot se mantiene (anti-enumeración).

### F3 — Algoritmos
- Ventana fija y deslizante: los rechazos no consumen cupo.
- `Retry-After` real (TTL restante, entrada más antigua + ventana, o tiempo al próximo token).
- El tope por endpoint (L4) se evalúa después de usuario/tenant y deja de aplicarse a H (ya lo cubre el overlay de tenant).
- Bugs: Growth cuenta doble (misma clave primaria y overlay), catálogo vacío cacheado 5 min tras un fallo, `hardOverridePerMinute` usado por ventana en Node.

### F4 — Cuotas
- Catálogo (§3) + data-migration de multiplicadores H en `PlanRateLimits`.
- Reestructurar `PreAuthByIp` del Gateway, `tenant-lookup` de Auth, `LoginThrottler`, limitadores de Connectors y el límite HTTP de Communication por usuario.
- Upload: solo el initiate consume cupo.

### F5 — Frontend CRM (`TaxVsion_Front`)
- Interceptor: 429/503 → un solo toast deduplicado con cuenta regresiva (*"You're making requests too quickly. Please try again in 30 seconds."* / *"We're handling a high volume of requests. Please try again in a few seconds."*).
- Reintento automático único de GET cuando la espera es corta.
- Un refresh con 429/503/red **ya no desloguea** (solo 400/401 del refresh).
- Mapeo en `toApiError`, que arregla los 239 usos de una vez.
- Los acks de socket rate-limited muestran un mensaje; los reintentos ad-hoc respetan el status.

### F6 — Portal (`CLIENTTAXPROFRONTEND`)
- Lo mismo que F5, y el modelo de error deja de filtrar la URL de la API.

### F7 — Throttles que pierden datos
- Webhooks de pago throttleados → 429 para que el proveedor reintente (nunca 200).
- Postmaster: por encima del límite → reintento diferido, no fallo permanente.

### F9 — Cierre
- Tests por clase en cada fase.
- Paneles/alertas Grafana: `ratelimit.blocked_total` por política, activaciones de load shedding.
- Config en `docker-compose.yml` / `deploy.yml` donde aplique.
- Actualizar `ADR_017` y la guía de nuevos servicios.
- Ejemplo Postman del 429.

### F8 — Fuera de alcance (mejora aparte)
- Chat sin N+1 (último mensaje en la lista de conversaciones).
- "Mark all read" en bulk.
- Caché de tenant lookup.
- Lista de Signature sin N+1.

## 5. Verificación y despliegue

- Por fase: csharpier acotado a los archivos tocados, gate del servicio, typecheck/Vitest en el front.
- Nada se commitea sin autorización del usuario.
- Orden de despliegue: F1 (mayor impacto y menor riesgo) → F2/F3 → F4 → F5/F6 → F7.
- Tras desplegar, calibrar los multiplicadores en `PlanRateLimits` con las métricas reales de prod (sin redeploy).

## 6. Estado — CERRADO 2026-09-25 (F1–F7 + F9)

| Fase | Resultado |
|---|---|
| F1 | Load shedding sin falsos positivos. Upgrade de socket por header, `/communication/socket.io` fuera de la medición, latencia hasta el primer byte, `ActivationSeconds` + `RecoveryRatio`, anónimos en su propia población. Test con Kestrel real. |
| F2 | Contrato único de rechazo (`RateLimitRejection`) en el tiered, en los 7 `AddRateLimiter` nativos y en Node. Throttles de usuario pasan a 429. CORS expone `Retry-After`. |
| F3 | Un rechazo no consume cupo. `Retry-After` real. Capa 4 al final y solo para I. Catálogo *last-good* ante fallos de Subscription. |
| F4 | Cuotas de §3. Migración de Subscription `AdjustPlanRateLimitMultipliersSearch` (H 2/5/10). Gateway `PreAuthByIp` 30/min sin `/auth/refresh`. `auth-refresh` 120/min. Communication 1000/IP + 600/usuario. |
| F5 | CRM (`TaxVsion_Front`): aviso global con cuenta regresiva, reintento silencioso de GET corto y refresh con 429/503/red sin logout. |
| F6 | Portal (`CLIENTTAXPROFRONTEND`): lo mismo que el CRM. Además, sin fuga de URL ni cuerpo crudo en los errores y el toast montado en la raíz (antes solo en 6 páginas). `SessionExpiryService.refreshFailed()` en los dos fronts. |
| F7 | Webhook de pago throttleado → 429 + evento `Failed` (no terminal). Postmaster por encima del cupo → diferido (rollback + reprogramación) hasta 60 entregas; el 429 por minuto de Connectors, igual. |
| F9 | Métrica `ratelimit.native_rejected_total{policy}`. Los anónimos del load shedding se etiquetan `anonymous`, no con la IP. 4 paneles y 3 alertas nuevas en Grafana. ADR_017 §2.4, guía §4.4–4.7 y ejemplos 429/503 en Postman. |

**Configuración de despliegue.** `deploy.yml` no cambia: ningún valor nuevo depende de un secret.
- Load shedding: literales en `docker-compose.yml` (gateway).
- Gateway `PreAuthByIp`: en `appsettings.json` del Gateway.
- Communication: defaults de `config.ts`.
- Cuotas tiered: en el catálogo (código) y en `PlanRateLimits` (DB).

**Qué redesplegar.**
- Backend: todos los servicios .NET, porque BuildingBlocks cambió (catálogo, contrato de rechazo, contadores Lua, métricas). También Communication (Node). La migración de Subscription corre en el deploy (ya está aplicada en local).
- Fronts: CRM y Portal.
- El dashboard y las alertas quedan provisionados en `deploy/observability`. **Ojo:** en el compose de producción Prometheus, Grafana, Loki y Tempo están comentados; el collector corre y descarta las métricas. Se verán cuando se reactive ese stack (o en local).

**Calibración en prod.** Con el stack de observabilidad activo, mirar en Grafana los paneles *Tasa de 429 por endpoint/policy* y *429 de limiters nativos por política*. Sin él, mirar los logs: el request log de Serilog registra cada respuesta 429/503 con su ruta, y el load shedding loguea su activación y desactivación.
- Si una política H o F sigue disparando para usuarios reales, ajustar su fila en `PlanRateLimits`, sin redeploy.
- Si dispara `gateway.pre_auth_by_ip` o `auth-refresh` desde la IP de una oficina, subir el `PermitLimit` en appsettings.
