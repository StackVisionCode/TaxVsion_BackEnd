// Carga sobre POST /payments-client/payments (ChargeTenantPaymentHandler, cobro directo sin
// PaymentLink) — el camino más caliente de PaymentClient en producción.
//
// Uso:
//   k6 run -e TENANT_JWT=... -e PAYMENT_METHOD_REF=pm_card_visa deploy/loadtests/charge-endpoint.js
//
// Ver README.md para el resto de las variables de entorno.
import http from 'k6/http';
import { check } from 'k6';
import { BASE_URL, authHeaders, requireEnv, uuid } from './lib/config.js';

const TENANT_JWT = requireEnv('TENANT_JWT');
const PAYMENT_METHOD_REF = __ENV.PAYMENT_METHOD_REF || 'pm_card_visa';

export const options = {
    scenarios: {
        steady_charges: {
            executor: 'constant-arrival-rate',
            rate: Number(__ENV.RATE || 20),
            timeUnit: '1s',
            duration: __ENV.DURATION || '2m',
            preAllocatedVUs: Number(__ENV.VUS || 30),
            maxVUs: Number(__ENV.MAX_VUS || 100),
        },
    },
    thresholds: {
        http_req_failed: ['rate<0.05'],
        http_req_duration: ['p(95)<2000'],
    },
};

export default function () {
    const body = JSON.stringify({
        providerCode: 'Stripe',
        amountCents: 1000 + Math.floor(Math.random() * 500),
        currency: 'USD',
        taxpayerId: null,
        purposeKind: 'Other',
        purposeExternalReferenceId: 'loadtest',
        paymentMethodReference: PAYMENT_METHOD_REF,
        receiptEmail: null,
        idempotencyKey: `loadtest-${uuid()}`,
    });

    const res = http.post(`${BASE_URL}/payments-client/payments`, body, authHeaders(TENANT_JWT));

    check(res, {
        'status is 200 or a handled provider decline (4xx)': (r) => r.status === 200 || (r.status >= 400 && r.status < 500),
    });
}
