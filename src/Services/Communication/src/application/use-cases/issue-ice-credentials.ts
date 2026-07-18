import { Result } from '../../domain/shared/result.js';
import type { TurnCredentialFactory } from '../ports/turn-credential-factory.js';

/**
 * Query publica: entrega ICE servers al cliente para el WebRTC. Incluye TURN
 * con credenciales HMAC efimeras (ttlSeconds). NUNCA devolvemos el static-auth-secret.
 */
export interface IssueIceCredentialsQuery {
  readonly tenantId: string;
  readonly userId: string;
  readonly ttlSeconds?: number;
}

export interface IssueIceCredentialsResult {
  readonly iceServers: readonly {
    readonly urls: string | readonly string[];
    readonly username?: string;
    readonly credential?: string;
  }[];
  readonly expiresAtUtc: string;
}

export function issueIceCredentials(
  query: IssueIceCredentialsQuery,
  deps: { turn: TurnCredentialFactory },
): Result<IssueIceCredentialsResult> {
  const ttl = query.ttlSeconds ?? 300;
  const issued = deps.turn.issue({ tenantId: query.tenantId, userId: query.userId, ttlSeconds: ttl });
  return Result.ok({ iceServers: issued.iceServers, expiresAtUtc: issued.expiresAtUtc });
}
