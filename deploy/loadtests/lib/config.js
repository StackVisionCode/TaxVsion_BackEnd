// Helpers compartidos por los scripts de deploy/loadtests/*.js — ver README.md de esta
// carpeta para las variables de entorno que cada script necesita.

export const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';

export function authHeaders(token) {
    return {
        headers: {
            'Content-Type': 'application/json',
            Authorization: `Bearer ${token}`,
        },
    };
}

export function requireEnv(name) {
    const value = __ENV[name];
    if (!value) {
        throw new Error(`Missing required env var ${name} — see deploy/loadtests/README.md.`);
    }
    return value;
}

export function uuid() {
    // No necesita ser criptográficamente fuerte — solo evitar colisiones de IdempotencyKey
    // dentro de una misma corrida de carga.
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
        const r = (Math.random() * 16) | 0;
        const v = c === 'x' ? r : (r & 0x3) | 0x8;
        return v.toString(16);
    });
}
