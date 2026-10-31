import { randomUUID } from 'node:crypto';
import type { InvitationCloseReason } from '../../domain/meetings/meeting-invitation.js';
import { MeetingEventType } from '../../domain/meetings/meeting-event-log.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import { MeetingEventTypes, type MeetingInvitationExpiredEvent } from '../../contracts/events/meeting-events.js';
import { recordMeetingEvent, type MeetingEventLogDeps } from './meeting-event-log-writer.js';

export interface CloseMeetingInvitationsInput {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly reason: InvitationCloseReason;
  readonly correlationId: string;
  readonly now?: Date;
}

export interface CloseMeetingInvitationsDeps extends MeetingEventLogDeps {
  readonly meetings: Pick<MeetingRepository, 'listInvitationsByMeeting' | 'saveInvitation'>;
  readonly publisher?: IntegrationEventPublisher | undefined;
}

/**
 * F6 (D-48): al terminar, cancelar o expirar la reunion, las invitaciones que seguian abiertas se
 * cierran (ya no entra nadie con ellas) y cada una deja `invitation.expired` + evento de integracion.
 * Best-effort: un fallo aqui nunca deshace el fin de la reunion. Devuelve cuantas cerro.
 */
export async function closeMeetingInvitations(input: CloseMeetingInvitationsInput, deps: CloseMeetingInvitationsDeps): Promise<number> {
  const now = input.now ?? new Date();
  let closed = 0;
  const invitations = await deps.meetings.listInvitationsByMeeting(input.tenantId, input.meetingId).catch(() => []);
  for (const invitation of invitations) {
    if (!invitation.close(now)) continue;
    closed += 1;
    const s = invitation.toSnapshot();
    await deps.meetings.saveInvitation(invitation);
    await recordMeetingEvent(deps, {
      tenantId: input.tenantId,
      meetingId: input.meetingId,
      type: MeetingEventType.InvitationExpired,
      targetUserId: s.inviteeUserId,
      payload: { invitationId: s.id, inviteeKind: s.inviteeKind, reason: input.reason },
      correlationId: input.correlationId,
      at: now,
    });
    if (deps.publisher) {
      const event: MeetingInvitationExpiredEvent = {
        eventId: randomUUID(),
        eventType: MeetingEventTypes.InvitationExpired,
        tenantId: input.tenantId,
        correlationId: input.correlationId,
        occurredOnUtc: now.toISOString(),
        invitationId: s.id,
        meetingId: input.meetingId,
        inviteeKind: s.inviteeKind,
        inviteeUserId: s.inviteeUserId,
        inviteeEmail: s.inviteeEmail,
        reason: input.reason,
        closedAtUtc: now.toISOString(),
      };
      await deps.publisher.enqueue(event).catch(() => undefined);
    }
  }
  return closed;
}
