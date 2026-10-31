import { z } from 'zod';

/**
 * Sesión del socket (INV-09 §13.2): el servidor cierra cada socket al `exp` de su token; el cliente
 * puede renovar sin reconectar con `auth.refresh` (mismo usuario y oficina, token nuevo) para que
 * una reunión mesh larga no pierda la señalización por un corte de socket.
 */
export const AuthRefreshPayloadSchema = z.object({
  token: z.string().min(20).max(8192),
});
export type AuthRefreshPayload = z.infer<typeof AuthRefreshPayloadSchema>;

export interface AuthRefreshResultDto {
  /** Nuevo vencimiento del socket (ISO-8601). */
  expiresAtUtc: string;
}

/** Aviso previo al cierre por `exp`: el cliente renueva ya o acepta la desconexión. */
export interface SessionExpiringDto {
  expiresAtUtc: string;
  secondsLeft: number;
}

export const SessionSocketEvents = {
  // c -> s
  AuthRefresh: 'auth.refresh',
  // s -> c
  SessionExpiring: 'session.expiring',
} as const;
