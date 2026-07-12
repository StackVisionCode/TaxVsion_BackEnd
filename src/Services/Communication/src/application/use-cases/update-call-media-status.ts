import { Result, makeError } from '../../domain/shared/result.js';
import { makeMediaStatus } from '../../domain/calls/media-status.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { CallMediaStatusDto } from '../../contracts/socket/call-socket-events.js';

/**
 * Comando: actualizar el media-status del propio participante. NO requiere
 * idempotencia (mismo estado = mismo efecto).
 *
 * Reglas:
 *   - Solo un participante puede modificar SU media-status.
 *   - Estado debe ser Accepted o Active — sin toggles fantasma tras hangup.
 */
export interface UpdateMediaStatusCommand {
  readonly tenantId: string;
  readonly callId: string;
  readonly actorUserId: string;
  readonly audioEnabled: boolean;
  readonly videoEnabled: boolean;
  readonly screenSharing: boolean;
}

export interface UpdateMediaStatusResult {
  readonly status: CallMediaStatusDto;
}

export async function updateCallMediaStatus(
  command: UpdateMediaStatusCommand,
  deps: { calls: CallRepository },
): Promise<Result<UpdateMediaStatusResult>> {
  const call = await deps.calls.findById(command.tenantId, command.callId);
  if (!call) return Result.fail(makeError('Call.NotFound', 'Call not found.'));

  const statusResult = makeMediaStatus({
    audioEnabled: command.audioEnabled,
    videoEnabled: command.videoEnabled,
    screenSharing: command.screenSharing,
  });
  if (!statusResult.isSuccess) return Result.fail(statusResult.error);

  const applyResult = call.applyMediaStatus({
    byUserId: command.actorUserId,
    status: statusResult.value,
  });
  if (!applyResult.isSuccess) return Result.fail(applyResult.error);

  await deps.calls.save(call);
  return Result.ok({
    status: {
      callId: command.callId,
      peerUserId: command.actorUserId,
      audioEnabled: command.audioEnabled,
      videoEnabled: command.videoEnabled,
      screenSharing: command.screenSharing,
    },
  });
}
