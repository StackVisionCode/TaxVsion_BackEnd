import { Result, makeError } from '../../domain/shared/result.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { CallStateDto } from '../../contracts/socket/call-socket-events.js';

/** Estados en los que la room de la llamada sigue viva y tiene sentido volver a ella. */
const OPEN_STATUSES: ReadonlySet<string> = new Set(['Ringing', 'Accepted', 'Active']);

/**
 * Tras reconectar el socket, un participante vuelve a la room de su llamada. Devuelve el estado actual:
 * si la llamada ya terminó (p. ej. la cerró el barrido de abandonadas), el cliente la cierra con ese estado.
 */
export async function rejoinCall(
  command: { tenantId: string; callId: string; userId: string },
  deps: { calls: CallRepository },
): Promise<Result<{ state: CallStateDto; open: boolean }>> {
  const call = await deps.calls.findById(command.tenantId, command.callId);
  if (!call || !call.isParticipant(command.userId)) {
    return Result.fail(makeError('Call.NotFound', 'Call not found.'));
  }
  const s = call.toSnapshot();
  return Result.ok({
    open: OPEN_STATUSES.has(s.status),
    state: {
      callId: s.id,
      status: s.status,
      endReason: s.endReason,
      durationSeconds: s.durationSeconds,
      updatedAtUtc: s.updatedAtUtc.toISOString(),
    },
  });
}
