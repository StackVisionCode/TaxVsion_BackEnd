import { Result, makeError } from '../../domain/shared/result.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { SfuService, TransportInfo, ConsumerInfo, RemoteProducerInfo } from '../ports/sfu-service.js';
import type { TurnCredentialFactory, IceServer } from '../ports/turn-credential-factory.js';
import type { types as MediasoupTypes } from 'mediasoup';

/**
 * Comandos de señalización SFU (mediasoup) para meetings con
 * `strategy === 'Sfu'` (mas de 4 participantes). Los objetos WebRTC
 * (DtlsParameters, RtpParameters, RtpCapabilities) son opacos al server —
 * mismo criterio que `data` en relay-meeting-signal.ts: no se re-validan a
 * mano, se dejan pasar a mediasoup y sus propios checks internos rechazan
 * lo malformado (try/catch abajo lo convierte en Result.fail).
 *
 * Guard comun a todos: el actor debe ser participante Joined del meeting.
 * mediasoup en si no sabe nada de "quien esta en el meeting" — esa es
 * responsabilidad exclusiva del `Meeting` aggregate, aca es donde se cruzan.
 */

type SfuDeps = { meetings: MeetingRepository; sfu: SfuService };

async function ensureJoined(deps: SfuDeps, tenantId: string, meetingId: string, userId: string): Promise<Result<void>> {
  const meeting = await deps.meetings.findById(tenantId, meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  if (!meeting.isJoinedParticipant(userId)) {
    return Result.fail(makeError('Meeting.Sfu.NotJoined', 'You must be joined to use the SFU.'));
  }
  return Result.okVoid();
}

// ---------- Router capabilities ----------

export async function getSfuRouterCapabilities(
  cmd: { tenantId: string; meetingId: string; userId: string },
  deps: SfuDeps,
): Promise<Result<MediasoupTypes.RtpCapabilities>> {
  const guard = await ensureJoined(deps, cmd.tenantId, cmd.meetingId, cmd.userId);
  if (!guard.isSuccess) return guard;
  try {
    const caps = await deps.sfu.getRouterRtpCapabilities(cmd.meetingId);
    return Result.ok(caps);
  } catch (err) {
    return Result.fail(makeError('Meeting.Sfu.RouterFailed', (err as Error).message));
  }
}

// ---------- Create transport ----------

export interface CreateSfuTransportResult extends TransportInfo {
  readonly iceServers: readonly IceServer[];
}

export async function createSfuTransport(
  cmd: { tenantId: string; meetingId: string; userId: string; direction: 'send' | 'recv' },
  deps: SfuDeps & { turn: TurnCredentialFactory },
): Promise<Result<CreateSfuTransportResult>> {
  const guard = await ensureJoined(deps, cmd.tenantId, cmd.meetingId, cmd.userId);
  if (!guard.isSuccess) return guard;
  try {
    const transport = await deps.sfu.createTransport({
      meetingId: cmd.meetingId,
      userId: cmd.userId,
      direction: cmd.direction,
    });
    // El transport en si (mediasoup, ICE-lite) siempre tiene IP publica —
    // no necesita TURN para si mismo. El TURN es para el LADO CLIENTE: le
    // damos las mismas credenciales coturn que ya usan las calls 1:1, para
    // que el browser pueda alcanzar al SFU si esta detras de un NAT/firewall
    // restrictivo. Mismo factory, mismas credenciales HMAC efimeras.
    const { iceServers } = deps.turn.issue({ tenantId: cmd.tenantId, userId: cmd.userId, ttlSeconds: 3600 });
    return Result.ok({ ...transport, iceServers });
  } catch (err) {
    return Result.fail(makeError('Meeting.Sfu.TransportFailed', (err as Error).message));
  }
}

// ---------- Connect transport ----------

export async function connectSfuTransport(
  cmd: {
    tenantId: string;
    meetingId: string;
    userId: string;
    transportId: string;
    dtlsParameters: MediasoupTypes.DtlsParameters;
  },
  deps: SfuDeps,
): Promise<Result<void>> {
  const guard = await ensureJoined(deps, cmd.tenantId, cmd.meetingId, cmd.userId);
  if (!guard.isSuccess) return guard;
  try {
    const ok = await deps.sfu.connectTransport(cmd);
    if (!ok) return Result.fail(makeError('Meeting.Sfu.TransportNotFound', 'Transport not found.'));
    return Result.okVoid();
  } catch (err) {
    return Result.fail(makeError('Meeting.Sfu.ConnectFailed', (err as Error).message));
  }
}

// ---------- Produce ----------

export async function produceSfuMedia(
  cmd: {
    tenantId: string;
    meetingId: string;
    userId: string;
    transportId: string;
    kind: MediasoupTypes.MediaKind;
    rtpParameters: MediasoupTypes.RtpParameters;
  },
  deps: SfuDeps,
): Promise<Result<{ producerId: string }>> {
  const guard = await ensureJoined(deps, cmd.tenantId, cmd.meetingId, cmd.userId);
  if (!guard.isSuccess) return guard;
  try {
    const result = await deps.sfu.produce(cmd);
    if (!result) return Result.fail(makeError('Meeting.Sfu.TransportNotFound', 'Send transport not found.'));
    return Result.ok(result);
  } catch (err) {
    return Result.fail(makeError('Meeting.Sfu.ProduceFailed', (err as Error).message));
  }
}

// ---------- Consume ----------

export async function consumeSfuMedia(
  cmd: {
    tenantId: string;
    meetingId: string;
    userId: string;
    transportId: string;
    producerId: string;
    rtpCapabilities: MediasoupTypes.RtpCapabilities;
  },
  deps: SfuDeps,
): Promise<Result<ConsumerInfo>> {
  const guard = await ensureJoined(deps, cmd.tenantId, cmd.meetingId, cmd.userId);
  if (!guard.isSuccess) return guard;
  try {
    const result = await deps.sfu.consume(cmd);
    if (!result) {
      return Result.fail(makeError('Meeting.Sfu.CannotConsume', 'Cannot consume this producer.'));
    }
    return Result.ok(result);
  } catch (err) {
    return Result.fail(makeError('Meeting.Sfu.ConsumeFailed', (err as Error).message));
  }
}

// ---------- Resume consumer ----------

export async function resumeSfuConsumer(
  cmd: { tenantId: string; meetingId: string; userId: string; consumerId: string },
  deps: SfuDeps,
): Promise<Result<void>> {
  const guard = await ensureJoined(deps, cmd.tenantId, cmd.meetingId, cmd.userId);
  if (!guard.isSuccess) return guard;
  try {
    const ok = await deps.sfu.resumeConsumer(cmd);
    if (!ok) return Result.fail(makeError('Meeting.Sfu.ConsumerNotFound', 'Consumer not found.'));
    return Result.okVoid();
  } catch (err) {
    return Result.fail(makeError('Meeting.Sfu.ResumeFailed', (err as Error).message));
  }
}

// ---------- List remote producers (para el recien llegado) ----------

export async function listSfuRemoteProducers(
  cmd: { tenantId: string; meetingId: string; userId: string },
  deps: SfuDeps,
): Promise<Result<readonly RemoteProducerInfo[]>> {
  const guard = await ensureJoined(deps, cmd.tenantId, cmd.meetingId, cmd.userId);
  if (!guard.isSuccess) return guard;
  return Result.ok(deps.sfu.listRemoteProducers(cmd.meetingId, cmd.userId));
}
