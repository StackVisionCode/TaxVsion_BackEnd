/**
 * Emisor de credenciales efimeras para TURN (Coturn). El backend NO expone el
 * static-auth-secret; entrega un `username=exp:userId` y `credential=HMAC(secret, username)`
 * para que el cliente use `turn:host:port` con esos valores hasta que expiren.
 *
 * Cierra CRIT-legacy del "fallback dummy" (username/credential='fallback') y
 * evita filtrar el secreto de larga vida en el cliente.
 */
export interface IceServer {
  readonly urls: string | readonly string[];
  readonly username?: string;
  readonly credential?: string;
}

export interface TurnCredentialFactory {
  issue(input: { tenantId: string; userId: string; ttlSeconds: number }): {
    readonly iceServers: readonly IceServer[];
    readonly expiresAtUtc: string;
  };
}
