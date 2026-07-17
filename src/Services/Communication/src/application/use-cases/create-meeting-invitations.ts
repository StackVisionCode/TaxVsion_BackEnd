import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { MeetingInvitation, type MeetingInviteeKind } from '../../domain/meetings/meeting-invitation.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import { MeetingEventTypes, type MeetingInvitationCreatedEvent } from '../../contracts/events/meeting-events.js';
import { config } from '../../infrastructure/config.js';

/** 7 dias — mas largo que el TTL del meeting en si (invitaciones a meetings recurrentes/futuros). */
const INVITATION_TTL_SECONDS = 60 * 60 * 24 * 7;

export interface CreateMeetingInvitationInput {
  readonly kind: MeetingInviteeKind;
  readonly userId?: string;
  readonly email?: string;
  readonly name?: string;
}

export interface CreateMeetingInvitationsCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly actorUserId: string;
  readonly invitees: readonly CreateMeetingInvitationInput[];
}

export interface CreateMeetingInvitationSummary {
  readonly id: string;
  /** Solo los primeros 8 chars — el token completo nunca se re-expone tras crearse. */
  readonly tokenPreview: string;
  readonly expiresAt: string;
  readonly inviteeEmail: string | null;
  readonly joinUrl: string;
}

export interface CreateMeetingInvitationsResult {
  readonly invitations: readonly CreateMeetingInvitationSummary[];
}

export interface CreateMeetingInvitationsDeps {
  readonly meetings: MeetingRepository;
  readonly publisher: IntegrationEventPublisher;
}

/**
 * Host/Cohost invitan N personas a un meeting. Cada invitee genera su propio
 * MeetingInvitation (token CSPRNG + hash), sin importar si es Employee/
 * Customer (tienen cuenta Auth — el token solo destraba isLocked) o External
 * (sin cuenta — entra 100% via join-by-token + shortLivedJoinTicket, Fase
 * Backend 5). Se valida el batch completo ANTES de persistir nada, para no
 * dejar invitaciones a medias si una entrada del batch es invalida.
 */
export async function createMeetingInvitations(
  command: CreateMeetingInvitationsCommand,
  deps: CreateMeetingInvitationsDeps,
): Promise<Result<CreateMeetingInvitationsResult>> {
  if (command.invitees.length === 0) {
    return Result.fail(makeError('Meeting.Invitation.EmptyBatch', 'At least one invitee is required.'));
  }

  const meeting = await deps.meetings.findById(command.tenantId, command.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));

  const canAct = command.actorUserId === meeting.hostUserId || meeting.isCohost(command.actorUserId);
  if (!canAct) return Result.fail(makeError('Meeting.HostOnly', 'Only host or cohost can invite.'));

  const now = new Date();
  const issued: Array<{ invitation: MeetingInvitation; plainToken: string }> = [];
  for (const invitee of command.invitees) {
    const issueResult = MeetingInvitation.issue({
      meetingId: command.meetingId,
      tenantId: command.tenantId,
      inviteeKind: invitee.kind,
      inviteeEmail: invitee.email ?? null,
      inviteeUserId: invitee.userId ?? null,
      inviteeName: invitee.name ?? null,
      ttlSeconds: INVITATION_TTL_SECONDS,
      now,
    });
    if (!issueResult.isSuccess) return Result.fail(issueResult.error);
    issued.push(issueResult.value);
  }

  const summaries: CreateMeetingInvitationSummary[] = [];
  for (const { invitation, plainToken } of issued) {
    await deps.meetings.saveInvitation(invitation);
    const snap = invitation.toSnapshot();
    const joinUrl = `${config.meetingInvitations.frontendBaseUrl}/join?token=${plainToken}`;

    const event: MeetingInvitationCreatedEvent = {
      eventId: randomUUID(),
      eventType: MeetingEventTypes.InvitationCreated,
      tenantId: command.tenantId,
      correlationId: command.correlationId,
      occurredOnUtc: snap.createdAtUtc.toISOString(),
      invitationId: snap.id,
      meetingId: snap.meetingId,
      inviteeKind: snap.inviteeKind,
      inviteeUserId: snap.inviteeUserId,
      inviteeEmail: snap.inviteeEmail,
      inviteeName: snap.inviteeName,
      tokenHash: snap.tokenHash,
      expiresAtUtc: snap.expiresAtUtc.toISOString(),
      joinUrl,
    };
    await deps.publisher.enqueue(event);

    summaries.push({
      id: snap.id,
      tokenPreview: `${plainToken.slice(0, 8)}...`,
      expiresAt: snap.expiresAtUtc.toISOString(),
      inviteeEmail: snap.inviteeEmail,
      joinUrl,
    });
  }

  return Result.ok({ invitations: summaries });
}
