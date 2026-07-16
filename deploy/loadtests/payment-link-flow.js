// Carga sobre el flujo completo de PaymentLink: creación autenticada (tenant) seguida del
// canje público (taxpayer, sin JWT) — GET + POST /payments-client/checkout/{token}. Cada
// iteración crea un link nuevo porque un link es de un solo uso (PaymentLink.MarkAsUsed);
// reusar uno solo entre VUs mediría el guardrail de "ya usado", no el camino feliz.
//
// Uso:
//   k6 run -e TENANT_JWT=... -e PAYMENT_METHOD_REF=pm_card_visa deploy/loadtests/payment-link-flow.js
//
// Ver README.md para el resto de las variables de entorno.
import http from 'k6/http';
import { check, sleep } from 'k6';
import { BASE_URL, authHeaders, requireEnv, uuid } from './lib/config.js';

const TENANT_JWT = requireEnv('TENANT_JWT');
const PAYMENT_METHOD_REF = __ENV.PAYMENT_METHOD_REF || 'pm_card_visa';

export const options = {
    scenarios: {
        link_lifecycle: {
            executor: 'ramping-vus',
            startVUs: 0,
            stages: [
                { duration: '30s', target: Number(__ENV.VUS || 20) },
                { duration: __ENV.DURATION || '2m', target: Number(__ENV.VUS || 20) },
                { duration: '15s', target: 0 },
            ],
        },
    },
    thresholds: {
        http_req_failed: ['rate<0.05'],
    },
};

export default function () {
    const createBody = JSON.stringify({
        taxpayerId: null,
        amountCents: 2500,
        currency: 'USD',
        purposeKind: 'Other',
        purposeExternalReferenceId: `loadtest-${uuid()}`,
        expiration: '01:00:00',
    });

    const createRes = http.post(`${BASE_URL}/payments-client/payment-links`, createBody, authHeaders(TENANT_JWT));
    const created = check(createRes, { 'link created (200)': (r) => r.status === 200 });
    if (!created) return;

    const token = createRes.json('token');
    if (!token) return;

    const getRes = http.get(`${BASE_URL}/payments-client/checkout/${token}`);
    check(getRes, { 'checkout page loads (200)': (r) => r.status === 200 });

    const payRes = http.post(
        `${BASE_URL}/payments-client/checkout/${token}/pay`,
        JSON.stringify({ providerPaymentMethodToken: PAYMENT_METHOD_REF, receiptEmail: null }),
        { headers: { 'Content-Type': 'application/json' } });

    check(payRes, {
        'redemption is 200 or a handled provider decline (4xx)': (r) => r.status === 200 || (r.status >= 400 && r.status < 500),
    });

    sleep(1);
}
