import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { MeetingInvitation } from '../../domain/meetings/meeting-invitation.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { PasscodeHasher } from '../ports/passcode-hasher.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { TurnCredentialFactory } from '../ports/turn-credential-factory.js';
import {
  MeetingEventTypes,
  type MeetingParticipantJoinedEvent,
} from '../../contracts/events/meeting-events.js';
import type { MeetingSnapshotDto } from '../../contracts/socket/meeting-socket-events.js';
import { participantSnapshotToDto } from './meeting-mappers.js';
import { ensureMeetingConversation } from './ensure-meeting-conversation.js';
import { issueIceCredentials, type IssueIceCredentialsResult } from './issue-ice-credentials.js';

export interface JoinMeetingCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly user: { userId: string; displayName: string; actorType?: string };
  readonly passcode?: string;
  readonly invitationToken?: string;
  /**
   * Fase Backend 5 — presente SOLO cuando el join viene de un guest conectado
   * con `auth.ticket` (ver build-io.ts). A diferencia de `invitationToken`
   * (plainToken, para usuarios con cuenta que destraban un meeting locked),
   * aca ya no hay plainToken disponible — el ticket solo lleva el id. La
   * invitation se re-carga por id y se consume via
   * MeetingInvitation.consumeForGuestJoin (revoked/used/expired, sin hash).
   */
  readonly guestInvitationId?: string;
  readonly audioDefault?: boolean;
  readonly videoDefault?: boolean;
}

export interface JoinMeetingResult {
  readonly snapshot: MeetingSnapshotDto;
  readonly requiresAdmission: boolean;
  /**
   * Fase Frontend 5 — un guest (auth.ticket) no puede llamar GET
   * /communication/webrtc/ice (exige un JWT real via app.authenticate, que
   * un guest nunca tiene). Se devuelve aca, reutilizando el socket YA
   * autenticado por el ticket, en vez de inventar un mecanismo de auth HTTP
   * nuevo. Usuarios con cuenta real lo reciben tambien (mismo shape) pero
   * pueden seguir usando el fetch HTTP existente si prefieren.
   */
  readonly iceServers: IssueIceCredentialsResult;
}

export interface JoinMeetingDeps {
  readonly meetings: MeetingRepository;
  readonly passcodes: PasscodeHasher;
  readonly publisher: IntegrationEventPublisher;
  readonly conversations: ConversationRepository;
  readonly turn: TurnCredentialFactory;
}

export async function joinMeeting(
  command: JoinMeetingCommand,
  deps: JoinMeetingDeps,
): Promise<Result<JoinMeetingResult>> {
  const meeting = await deps.meetings.findById(command.tenantId, command.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));

  let invitationValid = false;
  if (command.invitationToken) {
    const hash = MeetingInvitation.hash(command.invitationToken);
    const invitation = await deps.meetings.findInvitationByHash(hash);
    if (invitation) {
      const validation = invitation.validateForUse({
        plainToken: command.invitationToken,
        now: new Date(),
      });
      if (validation.isSuccess) {
        invitationValid = true;
        invitation.markUsed(new Date());
        await deps.meetings.saveInvitation(invitation);
      }
    }
  } else if (command.guestInvitationId) {
    const invitation = await deps.meetings.findInvitationById(command.tenantId, command.guestInvitationId);
    if (!invitation) {
      return Result.fail(makeError('Meeting.Invitation.NotFound', 'Invitation not found.'));
    }
    const consumeResult = invitation.consumeForGuestJoin(new Date());
    if (!consumeResult.isSuccess) return Result.fail(consumeResult.error);
    invitationValid = true;
    await deps.meetings.saveInvitation(invitation);
  }

  let passcodeMatch: boolean | null = null;
  if (meeting.requiresPasscode) {
    passcodeMatch = command.passcode
      ? await deps.passcodes.verify(meeting.passcodeHash!, command.passcode)
      : false;
  }

  const joinResult = meeting.requestJoin({
    userId: command.user.userId,
    displayName: command.user.displayName,
    ...(command.user.actorType !== undefined ? { actorType: command.user.actorType } : {}),
    hasValidInvitation: invitationValid,
    passcodeMatch,
    ...(command.audioDefault !== undefined ? { audioDefault: command.audioDefault } : {}),
    ...(command.videoDefault !== undefined ? { videoDefault: command.videoDefault } : {}),
  });
  if (!joinResult.isSuccess) return Result.fail(joinResult.error);

  await deps.meetings.save(meeting);
  const snapshot = meeting.toSnapshot();
  const yourRole = joinResult.value.role;
  const requiresAdmission = joinResult.value.requiresAdmission;

  let conversationId: string | null = null;
  if (!requiresAdmission) {
    const now = new Date();
    const event: MeetingParticipantJoinedEvent = {
      eventId: randomUUID(),
      eventType: MeetingEventTypes.ParticipantJoined,
      tenantId: command.tenantId,
      correlationId: command.correlationId,
      occurredOnUtc: now.toISOString(),
      meetingId: command.meetingId,
      participantUserId: command.user.userId,
      joinedAtUtc: now.toISOString(),
    };
    await deps.publisher.enqueue(event);

    // El chat del meeting es best-effort respecto del join en si — si falla
    // (bug, DB lenta) el usuario igual entra al meeting por WebRTC; solo se
    // queda sin chat hasta el proximo Join/reconnect. No abortamos el join.
    const chatResult = await ensureMeetingConversation(
      {
        tenantId: command.tenantId,
        correlationId: command.correlationId,
        meetingId: command.meetingId,
        meetingTitle: snapshot.title,
        member: {
          userId: command.user.userId,
          displayName: command.user.displayName,
          actorType: command.user.actorType ?? 'TenantEmployee',
        },
      },
      deps,
    );
    if (chatResult.isSuccess) {
      conversationId = chatResult.value.conversationId;
    }
  }

  const dto: MeetingSnapshotDto = {
    meetingId: snapshot.id,
    status: snapshot.status,
    strategy: snapshot.strategy,
    hostUserId: snapshot.hostUserId,
    isLocked: snapshot.isLocked,
    participants: snapshot.participants.map(participantSnapshotToDto),
    yourRole,
    sequence: 0,
    conversationId,
  };

  // Best-effort: `issueIceCredentials` no tiene ningun camino de fallo real
  // hoy (ver docblock de JoinMeetingResult.iceServers), pero si algun dia lo
  // tuviera, no tiene sentido tumbar un join ya exitoso por esto — el
  // cliente se queda sin TURN (WebRTC solo funciona en la misma LAN) en vez
  // de quedarse sin poder entrar al meeting.
  const iceResult = issueIceCredentials({ tenantId: command.tenantId, userId: command.user.userId }, deps);
  const iceServers = iceResult.isSuccess ? iceResult.value : { iceServers: [], expiresAtUtc: new Date().toISOString() };

  return Result.ok({ snapshot: dto, requiresAdmission, iceServers });
}
