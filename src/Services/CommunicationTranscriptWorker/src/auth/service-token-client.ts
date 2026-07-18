import { config } from '../config.js';
import { logger } from '../logger.js';

/**
 * M2M contra Auth — replica exacta de `SignatureServiceTokenAcquirer`
 * (Signature/Infrastructure/Sealing/HttpClients/SignatureServiceTokenAcquirer.cs):
 * NO es OAuth2 client_credentials estandar, es un POST JSON custom.
 *   POST {authBaseUrl}/auth/service-token
 *   body: { clientId, clientSecret, tenantId }
 *   -> { accessToken, expiresInSeconds, tokenType }
 * Cache en memoria por tenantId, refresh 30s antes de expirar (mismo buffer
 * que el .NET original).
 */
interface CachedToken {
  readonly accessToken: string;
  readonly expiresAtMs: number;
}

interface ServiceTokenResponse {
  readonly accessToken: string;
  readonly expiresInSeconds: number;
  readonly tokenType: string;
}

const REFRESH_BUFFER_MS = 30_000;

export class ServiceTokenClient {
  private readonly cache = new Map<string, CachedToken>();

  async getToken(tenantId: string): Promise<string> {
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

    const data = (await response.json()) as ServiceTokenResponse;
    const token: CachedToken = {
      accessToken: data.accessToken,
      expiresAtMs: Date.now() + data.expiresInSeconds * 1000,
    };
    this.cache.set(tenantId, token);
    return token.accessToken;
  }
}
