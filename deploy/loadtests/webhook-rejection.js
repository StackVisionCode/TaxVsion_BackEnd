// Verifica que el rate limiter HTTP de los webhooks (§28.4/§K.1 — 1000 req/min/IP,
// FixedWindowRateLimiter) realmente entra en acción. El payload y la firma son basura a
// propósito: lo que se mide acá es el rechazo ANTES de llegar al handler (429 del
// middleware), no la verificación de firma (400 del handler) — por eso el propio volumen de
// requests, no su contenido, es lo que importa.
//
// Corre desde una sola máquina (una sola IP de origen) — si se corre distribuido
// (k6 cloud, múltiples runners) el límite por IP nunca se alcanza y el test no sirve.
//
// Uso:
//   k6 run -e PAYMENTAPP_ONLY=1 deploy/loadtests/webhook-rejection.js
//
// Ver README.md para el resto de las variables de entorno.
import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';
import { BASE_URL } from './lib/config.js';

const throttled429 = new Counter('rate_limiter_429_total');

const GARBAGE_BODY = JSON.stringify({ id: 'loadtest', type: 'payment_intent.succeeded' });
const HEADERS = { headers: { 'Content-Type': 'application/json', 'Stripe-Signature': 't=0,v1=deadbeef' } };

// Bien por encima de 1000/min repartido entre los targets — el objetivo es cruzar el límite
// rápido dentro de la misma ventana de 1 minuto, no sostener carga larga.
export const options = {
    scenarios: {
        webhook_flood: {
            executor: 'constant-arrival-rate',
            rate: Number(__ENV.RATE || 40),
            timeUnit: '1s',
            duration: __ENV.DURATION || '45s',
            preAllocatedVUs: Number(__ENV.VUS || 50),
            maxVUs: Number(__ENV.MAX_VUS || 200),
        },
    },
};

const PAYMENT_APP_ONLY = __ENV.PAYMENTAPP_ONLY === '1';
const TENANT_ID = __ENV.TENANT_ID || '00000000-0000-0000-0000-000000000000';

export default function () {
    const res = http.post(`${BASE_URL}/payments-app/webhooks/stripe`, GARBAGE_BODY, HEADERS);
    check(res, {
        'paymentapp: 400 (bad signature) before the limit, 429 after': (r) => r.status === 400 || r.status === 429,
    });
    if (res.status === 429) throttled429.add(1, { service: 'paymentapp' });

    if (!PAYMENT_APP_ONLY) {
        const clientRes = http.post(`${BASE_URL}/payments-client/webhooks/${TENANT_ID}/stripe`, GARBAGE_BODY, HEADERS);
        check(clientRes, {
            'paymentclient: 400 (bad signature) before the limit, 429 after': (r) => r.status === 400 || r.status === 429,
        });
        if (clientRes.status === 429) throttled429.add(1, { service: 'paymentclient' });
    }
}

// El summary por defecto de k6 no separa 429 de otros 4xx — sin esto no habría forma de
// confirmar, al final de la corrida, que el limiter realmente cortó tráfico (en vez de solo
// rechazar por firma inválida durante toda la ventana).
export function handleSummary(data) {
    const total = data.metrics.rate_limiter_429_total ? data.metrics.rate_limiter_429_total.values.count : 0;
    console.log(`rate_limiter_429_total = ${total} (esperado: > 0 si RATE*DURATION supera 1000/min/IP)`);
    return { stdout: JSON.stringify(data, null, 2) };
}
