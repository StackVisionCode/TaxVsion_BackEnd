import { config } from '../config.js';
import { logger } from '../logger.js';
const REFRESH_BUFFER_MS = 30_000;
export class ServiceTokenClient {
    cache = new Map();
    async getToken(tenantId) {
        const cached = this.cache.get(tenantId);
        if (cached && cached.expiresAtMs - REFRESH_BUFFER_MS > Date.now()) {
            return cached.accessToken;
        }
        const response = await fetch(`${config.auth.baseUrl}/auth/service-token`, {
            method: 'POST',
            headers: { 'content-type': 'application/json' },
            body: JSON.stringify({
                clientId: config.auth.clientId,
                clientSecret: config.auth.clientSecret,
                tenantId,
            }),
        });
        if (!response.ok) {
            const body = await response.text().catch(() => '');
            logger.error({ status: response.status, body: body.slice(0, 300) }, 'service-token request failed');
            throw new Error(`Auth service-token request failed with status ${response.status}`);
        }
        const data = (await response.json());
        const token = {
            accessToken: data.accessToken,
            expiresAtMs: Date.now() + data.expiresInSeconds * 1000,
        };
        this.cache.set(tenantId, token);
        return token.accessToken;
    }
}
//# sourceMappingURL=service-token-client.js.map