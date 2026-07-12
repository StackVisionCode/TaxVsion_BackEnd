import { createHash, createHmac } from 'node:crypto';
import type { IceServer, TurnCredentialFactory } from '../../application/ports/turn-credential-factory.js';
import { config } from '../config.js';
import { logger } from '../logger/logger.js';

/**
 * Coturn `use-auth-secret` mechanism (rfc5389 + Coturn extension). El backend
 * expide un `username=exp:hashedId` (unix ts + SHA-256 truncado de tenant+user)
 * y un `credential=base64(HMAC-SHA1(secret, username))`. Coturn valida sin
 * lookups. El hash del userId es una higiene basica para no exponer el UUID
 * real del usuario en los logs de coturn ni en la traza WebRTC del cliente.
 *
 * ICE servers devueltos (en orden de preferencia para el peer):
 *   1. STUN del propio coturn (`stun:<host>[:port]`) — misma infraestructura,
 *      menor latencia que STUN publicos.
 *   2. TURN autenticado (`turn:<host>[:port]?transport=udp` y `?transport=tcp`
 *      si se declararon en la URL) — necesario para peers detras de NAT
 *      simetrico.
 *   3. STUN Google publico — ultimo fallback si el TURN privado esta caido.
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
      logger.warn({ tenantId: input.tenantId }, 'TURN not configured — returning public STUN only');
      return {
        iceServers: [{ urls: 'stun:stun.l.google.com:19302' }],
        expiresAtUtc,
      };
    }

    // 16 bytes = 128 bits of derived identity, enough for coturn quota tracking
    // without exposing the raw UUID of the user.
    const hashedId = createHash('sha256')
      .update(`${input.tenantId}:${input.userId}`)
      .digest('hex')
      .slice(0, 32);
    const username = `${expiresUnix}:${hashedId}`;
    const credential = createHmac('sha1', secret).update(username).digest('base64');

    const stunFromTurn = deriveStunFromTurn(turnUrl);
    const iceServers: IceServer[] = [];
    if (stunFromTurn) iceServers.push({ urls: stunFromTurn });
    iceServers.push({ urls: turnUrl, username, credential });
    iceServers.push({ urls: 'stun:stun.l.google.com:19302' });

    return { iceServers, expiresAtUtc };
  }
}

/**
 * Converts a coturn `turn:host:port?transport=udp` URL into a matching
 * `stun:host:port` URL so the client can use the same infrastructure for
 * STUN binding requests. Returns null if the input doesn't parse as a TURN URL.
 */
function deriveStunFromTurn(turnUrl: string): string | null {
  // coturn URL forms:
  //   turn:host                       → stun:host
  //   turn:host:port                  → stun:host:port
  //   turn:host:port?transport=udp    → stun:host:port
  //   turns:host:port?transport=tcp   → stun:host:port  (TLS-only TURN still uses plain STUN)
  const match = turnUrl.match(/^turns?:([^?]+)/i);
  if (!match) return null;
  return `stun:${match[1]}`;
}
