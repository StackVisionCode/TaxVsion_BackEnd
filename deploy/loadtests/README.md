# Load tests — PaymentApp / PaymentClient (§K.2)

Scripts de [k6](https://k6.io/docs/get-started/installation/) contra los caminos calientes de
los dos microservicios de pago. El diseño (§K.2) no especifica escenarios concretos más allá
de "load tests" — esta carpeta es una interpretación pragmática: cuatro escenarios que cubren
el cobro directo, el ciclo de vida completo de un PaymentLink, las lecturas admin cross-tenant,
y el rate limiter de webhooks agregado en §K.1.

No son parte del build ni de `dotnet test` — se corren manualmente, contra un stack levantado,
con credenciales reales (JWTs válidos y, si se usa Stripe de verdad, claves de test).

## Requisitos

- [k6](https://k6.io/docs/get-started/installation/) instalado (`k6 version`).
- El stack corriendo (`docker compose -f deploy/docker/docker-compose.yml up -d`).
- El `gateway` no publica puerto al host por diseño (Caddy es el único entry point público —
  ver `deploy/docker/docker-compose.yml`). Para pegarle desde el host durante una corrida de
  carga, la forma más simple es publicar el puerto temporalmente:

  ```powershell
  docker compose -f deploy/docker/docker-compose.yml `
    -f deploy/loadtests/docker-compose.override.yml up -d gateway
  ```

  (ver `docker-compose.override.yml` en esta carpeta — solo agrega `ports: ["8080:8080"]` al
  servicio `gateway`; no tocar el compose de producción). Revertir con
  `docker compose -f deploy/docker/docker-compose.yml up -d gateway` al terminar.

- Un JWT de tenant válido con los permisos `payment_client.payment.charge`,
  `payment_client.payment_link.manage` y `payment_client.payment_link.read` (para
  `charge-endpoint.js` y `payment-link-flow.js`).
- Un JWT de admin de plataforma válido (para `admin-search.js`).
- Si `Stripe__SecretKey`/`Stripe__PlatformSecretKey` del stack apuntan a modo test de Stripe,
  `pm_card_visa` (el payment method de test que Stripe expone para pegarle a la API sin pasar
  por Elements) funciona sin tokenizar nada real. Si el stack usa `Manual` como provider por
  defecto, cualquier string sirve como `PAYMENT_METHOD_REF`.

## Scripts

| Script | Qué mide | Variables requeridas |
|---|---|---|
| `charge-endpoint.js` | `POST /payments-client/payments` (cobro directo) bajo carga sostenida | `TENANT_JWT` |
| `payment-link-flow.js` | Creación + canje de un `PaymentLink` (un link nuevo por iteración, ya que es de un solo uso) | `TENANT_JWT` |
| `admin-search.js` | Lecturas cross-tenant (`/payments-app/admin/payments`, `/payments-client/admin/payments`) — el camino sin filtro de tenant, el más caro | `ADMIN_JWT` |
| `webhook-rejection.js` | Confirma que el `FixedWindowRateLimiter` de §K.1 corta tráfico a 1000 req/min/IP — debe correrse desde una sola máquina (una sola IP de origen) | ninguna (opcional: `TENANT_ID`) |

Variables opcionales comunes: `BASE_URL` (default `http://localhost:8080`), `VUS`, `DURATION`,
`RATE`.

## Correr

```powershell
k6 run -e TENANT_JWT=$env:TENANT_JWT -e PAYMENT_METHOD_REF=pm_card_visa deploy/loadtests/charge-endpoint.js
k6 run -e TENANT_JWT=$env:TENANT_JWT deploy/loadtests/payment-link-flow.js
k6 run -e ADMIN_JWT=$env:ADMIN_JWT deploy/loadtests/admin-search.js
k6 run deploy/loadtests/webhook-rejection.js
```

## Interpretando `webhook-rejection.js`

El body y la firma son basura a propósito — el objetivo no es probar el parseo de Stripe, es
confirmar que el middleware de rate limiting (`app.UseRateLimiter()`, agregado en §K.1) corta
tráfico ANTES de llegar al handler una vez superado el límite. El script imprime al final
`rate_limiter_429_total`: si `RATE * DURATION` (requests totales) supera 1000 dentro de la
ventana de un minuto y ese contador sigue en 0, el limiter no está funcionando como se espera
y hay que revisar `Program.cs` de ambos servicios.
