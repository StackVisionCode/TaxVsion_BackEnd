import { createHmac } from 'node:crypto';
import type { IceServer, TurnCredentialFactory } from '../../application/ports/turn-credential-factory.js';
import { config } from '../config.js';
import { logger } from '../logger/logger.js';

/**
 * Coturn `use-auth-secret` mechanism (rfc5389 + Coturn extension). El backend
 * expide un `username=exp:userId` (unix ts + userId) y un
 * `credential=base64(HMAC-SHA1(secret, username))`. Coturn valida sin lookups.
 *
 * Cierra CRIT-legacy de fallback dummy: si el secret no esta configurado se
 * devuelven solo STUN publicos y se log-warn — NUNCA credenciales falsas.
 */
export class HmacTurnCredentialFactory implements TurnCredentialFactory {
  issue(input: {
    tenantId: string;
    userId: string;
    ttlSeconds: number;
  }): { readonly iceServers: readonly IceServer[]; readonly expiresAtUtc: string } {
    const secret = config.turn.staticAuthSecret;
    const turnUrl = config.turn.url;
    const expiresUnix = Math.floor(Date.now() / 1000) + input.ttlSeconds;
    const expiresAtUtc = new Date(expiresUnix * 1000).toISOString();

    if (!secret || !turnUrl) {
      logger.warn({ tenantId: input.tenantId }, 'TURN not configured — returning STUN only');
      return {
        iceServers: [{ urls: 'stun:stun.l.google.com:19302' }],
        expiresAtUtc,
      };
    }

    const username = `${expiresUnix}:${input.userId}`;
    const credential = createHmac('sha1', secret).update(username).digest('base64');
    return {
      iceServers: [
        { urls: 'stun:stun.l.google.com:19302' },
        { urls: turnUrl, username, credential },
      ],
      expiresAtUtc,
    };
  }
}
