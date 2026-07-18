// Carga de lectura sobre los endpoints admin cross-tenant (§J.2/§42.6) de ambos servicios —
// PaymentAppAdminController y PaymentClientAdminController. Simula un dashboard admin
// paginando resultados; el objetivo es medir el costo de las queries cross-tenant, que no
// tienen el filtro de TenantId habitual y por lo tanto son las más pesadas del sistema.
//
// Uso:
//   k6 run -e ADMIN_JWT=... deploy/loadtests/admin-search.js
//
// Ver README.md para el resto de las variables de entorno.
import http from 'k6/http';
import { check } from 'k6';
import { BASE_URL, authHeaders, requireEnv } from './lib/config.js';

const ADMIN_JWT = requireEnv('ADMIN_JWT');

export const options = {
    scenarios: {
        admin_reads: {
            executor: 'constant-vus',
            vus: Number(__ENV.VUS || 10),
            duration: __ENV.DURATION || '2m',
        },
    },
    thresholds: {
        http_req_failed: ['rate<0.01'],
        http_req_duration: ['p(95)<1500'],
    },
};

export default function () {
    const page = 1 + Math.floor(Math.random() * 5);
    const opts = authHeaders(ADMIN_JWT);

    const paymentAppRes = http.get(`${BASE_URL}/payments-app/admin/payments?page=${page}&pageSize=50`, opts);
    check(paymentAppRes, { 'payment-app admin search 200': (r) => r.status === 200 });

    const paymentClientRes = http.get(`${BASE_URL}/payments-client/admin/payments?page=${page}&pageSize=50`, opts);
    check(paymentClientRes, { 'payment-client admin search 200': (r) => r.status === 200 });
}
