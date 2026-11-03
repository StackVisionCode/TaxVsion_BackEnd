import { Result, makeError } from '../../domain/shared/result.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';

export interface ResolveMeetingCodeForMemberCommand {
  readonly tenantId: string;
  readonly shortCode: string;
}

/** Lo que la oficina necesita para entrar con el codigo por el lobby normal (con el id real). */
export interface MeetingCodeResolution {
  readonly meetingId: string;
  readonly title: string;
  readonly hostDisplayName: string;
  readonly status: string;
  readonly requiresPasscode: boolean;
  readonly isLocked: boolean;
}

export interface ResolveMeetingCodeForMemberDeps {
  readonly meetings: Pick<MeetingRepository, 'findByShortCode'>;
}

/**
 * MNT-01: "Join with code" del CRM. A diferencia de `GET by-code` (publico, entre oficinas, sin id),
 * esta es autenticada y solo busca en la oficina de quien pregunta: un codigo de otra oficina es
 * NotFound. Lock, passcode y sala de espera se siguen comprobando en el join.
 */
export async function resolveMeetingCodeForMember(
  command: ResolveMeetingCodeForMemberCommand,
  deps: ResolveMeetingCodeForMemberDeps,
): Promise<Result<MeetingCodeResolution>> {
  const shortCode = normalizeShortCode(command.shortCode);
  if (!shortCode) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const meeting = await deps.meetings.findByShortCode(command.tenantId, shortCode);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const snap = meeting.toSnapshot();
  return Result.ok({
    meetingId: snap.id,
    title: snap.title,
    hostDisplayName: snap.participants.find((p) => p.userId === snap.hostUserId)?.displayName ?? 'Host',
    status: snap.status,
    requiresPasscode: meeting.requiresPasscode,
    isLocked: snap.isLocked,
  });
}

/** El codigo se dicta o se pega: sin espacios ni guiones y en mayusculas. */
export function normalizeShortCode(raw: string): string {
  return raw.replace(/[\s-]/g, '').toUpperCase();
}
