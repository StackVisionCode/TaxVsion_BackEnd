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
  type MeetingInvitationUsedEvent,
  type MeetingParticipantJoinedEvent,
  type MeetingParticipantRejoinedEvent,
} from '../../contracts/events/meeting-events.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';
import type { MeetingSnapshotDto } from '../../contracts/socket/meeting-socket-events.js';
import { buildMeetingSnapshotDto } from './meeting-mappers.js';
import { resolveAvatars, type AvatarResolver } from './resolve-avatars.js';
import { ensureMeetingConversation } from './ensure-meeting-conversation.js';
import { issueIceCredentials, type IssueIceCredentialsResult } from './issue-ice-credentials.js';
import type { CustomerPortalAccountRepository } from '../ports/customer-portal-account-repository.js';
import { MeetingEventType } from '../../domain/meetings/meeting-event-log.js';
import { recordMeetingEvent, type MeetingEventLogDeps } from './meeting-event-log-writer.js';

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
  /** F1: ya estaba Joined y volvio a entrar con media nueva (joinOrder renovado). */
  readonly rejoined: boolean;
  /** F5 (D-49): el re-join completo bajo la mano que tenia levantada; el cliente avisa. */
  readonly handLoweredOnRejoin: boolean;
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

export interface JoinMeetingDeps extends MeetingEventLogDeps {
  readonly meetings: MeetingRepository;
  /** D-58: un usuario del portal entra con su CustomerId persistido en el participante. */
  readonly customerPortalAccounts?: Pick<CustomerPortalAccountRepository, 'findActiveByUserId'> | undefined;
  readonly passcodes: PasscodeHasher;
  readonly publisher: IntegrationEventPublisher;
  readonly conversations: ConversationRepository;
  readonly turn: TurnCredentialFactory;
  /** F5: fotos de los participantes del snapshot. Opcional (sin el, salen sin `avatarFileId`). */
  readonly avatars?: AvatarResolver;
  /** F6 (D-47): sala de espera forzada para guests segun la politica de la oficina. */
  readonly effectiveSettings?: Pick<EffectiveSettingsResolver, 'resolve'> | undefined;
}

export async function joinMeeting(
  command: JoinMeetingCommand,
  deps: JoinMeetingDeps,
): Promise<Result<JoinMeetingResult>> {
  // `meetingId` puede venir como id real o como CODIGO CORTO: el camino "entrar con un codigo"
  // (§5.2 camino 1) no tiene forma de conocer el id, porque `GET /meetings/by-code/:code` devuelve
  // titulo y host a proposito, nunca el id. Sin esta resolucion ese camino respondia
  // `Meeting.NotFound` SIEMPRE — `findByShortCode` estaba implementado pero no lo llamaba nadie.
  //
  // El fallback es tenant-scoped igual que la busqueda por id, asi que un codigo de otra oficina
  // sigue sin entrar. Y no abre nada por si solo: lock, passcode e invitacion se comprueban despues,
  // sobre la reunion ya resuelta.
  const meeting =
    (await deps.meetings.findById(command.tenantId, command.meetingId)) ??
    (await deps.meetings.findByShortCode(command.tenantId, command.meetingId));
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));

  // A partir de aca SIEMPRE el id real: `command.meetingId` puede ser un codigo corto, y usarlo
  // publicaria eventos y creraria la conversacion del chat con una clave que no es la del meeting,
  // ademas de rechazar invitaciones validas.
  const meetingId = meeting.toSnapshot().id;

  let invitationValid = false;
  // F6: consumo del uso unico del guest, diferido hasta que el join quede guardado.
  let afterJoinSaved: (() => Promise<void>) | null = null;
  if (command.invitationToken) {
    const hash = MeetingInvitation.hash(command.invitationToken);
    const invitation = await deps.meetings.findInvitationByHash(hash);
    // La invitacion se resuelve por el hash del token, no por el meeting: sin estas dos
    // comprobaciones un token de un meeting servia para entrar a cualquier otro, y un token
    // dirigido a una persona servia a cualquiera que lo tuviera en la mano.
    if (invitation && isInvitationForThisJoin(invitation, command, meetingId)) {
      const validation = invitation.validateForUse({
        plainToken: command.invitationToken,
        now: new Date(),
      });
      if (validation.isSuccess) {
        invitationValid = true;
        // F6 (D-48): el token sigue valiendo para este usuario; solo se deja rastro del uso.
        const usedAt = new Date();
        invitation.recordUse(usedAt);
        await deps.meetings.saveInvitation(invitation);
        await noteInvitationUse(invitation, command, meetingId, usedAt, deps);
      }
    }
  } else if (command.guestInvitationId) {
    const invitation = await deps.meetings.findInvitationById(command.tenantId, command.guestInvitationId);
    // Mismo motivo: el ticket de guest lleva el meeting para el que se emitio, pero el meetingId
    // del join lo manda el cliente.
    if (!invitation || invitation.toSnapshot().meetingId !== meetingId) {
      return Result.fail(makeError('Meeting.Invitation.NotFound', 'Invitation not found.'));
    }
    // F6 (D-48): el guest que sigue dentro (gracia) o en la sala de espera vuelve a entrar sin consumir
    // otra vez su unico uso; una revocacion en medio si lo deja fuera.
    const guestInside = meeting
      .toSnapshot()
      .participants.some((p) => p.userId === command.user.userId && (p.status === 'Joined' || p.status === 'Waiting'));
    if (guestInside && invitation.toSnapshot().usedAtUtc !== null) {
      if (invitation.toSnapshot().revokedAtUtc !== null) {
        return Result.fail(makeError('Meeting.Invitation.Revoked', 'Invitation was revoked.'));
      }
    } else {
      const usedAt = new Date();
      const consumeResult = invitation.consumeForGuestJoin(usedAt);
      if (!consumeResult.isSuccess) return Result.fail(consumeResult.error);
      // El unico uso se persiste DESPUES de guardar el join: si el join falla, el enlace sigue valiendo.
      afterJoinSaved = async () => {
        await deps.meetings.saveInvitation(invitation);
        await noteInvitationUse(invitation, command, meetingId, usedAt, deps);
      };
    }
    invitationValid = true;
  }

  let passcodeMatch: boolean | null = null;
  if (meeting.requiresPasscode) {
    passcodeMatch = command.passcode
      ? await deps.passcodes.verify(meeting.passcodeHash!, command.passcode)
      : false;
  }

  const customerId =
    command.user.actorType === 'CustomerPortal' && deps.customerPortalAccounts
      ? ((await deps.customerPortalAccounts.findActiveByUserId(command.user.userId).catch(() => null))?.customerId ?? null)
      : null;
  // F6 (D-47): la politica de la oficina manda a los guests a la sala de espera.
  const forceWaitingRoom =
    command.user.actorType === 'Guest' && deps.effectiveSettings
      ? (await deps.effectiveSettings.resolve(command.tenantId)).meeting.guestsWaitingRoom
      : false;
  const joinResult = meeting.requestJoin({
    userId: command.user.userId,
    displayName: command.user.displayName,
    ...(command.user.actorType !== undefined ? { actorType: command.user.actorType } : {}),
    customerId,
    hasValidInvitation: invitationValid,
    passcodeMatch,
    forceWaitingRoom,
    ...(command.audioDefault !== undefined ? { audioDefault: command.audioDefault } : {}),
    ...(command.videoDefault !== undefined ? { videoDefault: command.videoDefault } : {}),
  });
  if (!joinResult.isSuccess) return Result.fail(joinResult.error);

  await deps.meetings.save(meeting);
  if (afterJoinSaved) await afterJoinSaved();
  const snapshot = meeting.toSnapshot();
  const yourRole = joinResult.value.role;
  const requiresAdmission = joinResult.value.requiresAdmission;
  const rejoined = joinResult.value.rejoined;
  const handLoweredOnRejoin = joinResult.value.handLoweredOnRejoin;

  let conversationId: string | null = null;
  await recordMeetingEvent(deps, {
    tenantId: command.tenantId,
    meetingId,
    type: requiresAdmission
      ? MeetingEventType.ParticipantWaiting
      : rejoined
        ? MeetingEventType.ParticipantRejoined
        : MeetingEventType.ParticipantJoined,
    actorUserId: command.user.userId,
    payload: { actorType: command.user.actorType ?? 'TenantEmployee' },
    correlationId: command.correlationId,
  });
  if (handLoweredOnRejoin) {
    await recordMeetingEvent(deps, {
      tenantId: command.tenantId,
      meetingId,
      type: MeetingEventType.HandLowered,
      actorUserId: command.user.userId,
      payload: { reason: 'rejoin' },
      correlationId: command.correlationId,
    });
  }
  if (!requiresAdmission) {
    const now = new Date();
    await deps.publisher.enqueue(rejoined ? rejoinedEvent(command, meetingId, snapshot, now) : joinedEvent(command, meetingId, now));

    // El chat del meeting es best-effort respecto del join en si — si falla
    // (bug, DB lenta) el usuario igual entra al meeting por WebRTC; solo se
    // queda sin chat hasta el proximo Join/reconnect. No abortamos el join.
    const chatResult = await ensureMeetingConversation(
      {
        tenantId: command.tenantId,
        correlationId: command.correlationId,
        meetingId,
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

  const avatars = await resolveAvatars(
    deps.avatars,
    snapshot.participants.map((p) => p.userId),
  );
  const dto = buildMeetingSnapshotDto(snapshot, yourRole, conversationId, avatars);

  // Best-effort: `issueIceCredentials` no tiene ningun camino de fallo real
  // hoy (ver docblock de JoinMeetingResult.iceServers), pero si algun dia lo
  // tuviera, no tiene sentido tumbar un join ya exitoso por esto — el
  // cliente se queda sin TURN (WebRTC solo funciona en la misma LAN) en vez
  // de quedarse sin poder entrar al meeting.
  const iceResult = issueIceCredentials({ tenantId: command.tenantId, userId: command.user.userId }, deps);
  const iceServers = iceResult.isSuccess ? iceResult.value : { iceServers: [], expiresAtUtc: new Date().toISOString() };

  return Result.ok({ snapshot: dto, requiresAdmission, rejoined, handLoweredOnRejoin, iceServers });
}

function joinedEvent(command: JoinMeetingCommand, meetingId: string, now: Date): MeetingParticipantJoinedEvent {
  return {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.ParticipantJoined,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: now.toISOString(),
    meetingId,
    participantUserId: command.user.userId,
    joinedAtUtc: now.toISOString(),
  };
}

function rejoinedEvent(
  command: JoinMeetingCommand,
  meetingId: string,
  snapshot: { participants: readonly { userId: string; joinOrder: number }[] },
  now: Date,
): MeetingParticipantRejoinedEvent {
  const me = snapshot.participants.find((p) => p.userId === command.user.userId);
  return {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.ParticipantRejoined,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: now.toISOString(),
    meetingId,
    participantUserId: command.user.userId,
    joinOrder: me?.joinOrder ?? 0,
    rejoinedAtUtc: now.toISOString(),
  };
}

/**
 * La invitacion tiene que ser de ESTE meeting y, si fue dirigida a un usuario concreto, de ESE
 * usuario. Una invitacion abierta (solo por email, sin userId) no ata al portador: el email no se
 * verifica en este flujo, asi que exigirlo dejaria fuera a invitados legitimos.
 */
/** F6 (T-64): rastro de cada uso de invitacion (log + evento), para cuenta y para guest. */
async function noteInvitationUse(
  invitation: MeetingInvitation,
  command: JoinMeetingCommand,
  meetingId: string,
  usedAt: Date,
  deps: JoinMeetingDeps,
): Promise<void> {
  const s = invitation.toSnapshot();
  await recordMeetingEvent(deps, {
    tenantId: command.tenantId,
    meetingId,
    type: MeetingEventType.InvitationUsed,
    actorUserId: command.user.userId,
    payload: { invitationId: s.id, inviteeKind: s.inviteeKind, useCount: s.useCount },
    correlationId: command.correlationId,
    at: usedAt,
  });
  const event: MeetingInvitationUsedEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.InvitationUsed,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: usedAt.toISOString(),
    invitationId: s.id,
    meetingId,
    inviteeKind: s.inviteeKind,
    inviteeUserId: s.inviteeUserId,
    usedByUserId: command.user.userId,
    usedAtUtc: usedAt.toISOString(),
    useCount: s.useCount,
  };
  await deps.publisher.enqueue(event).catch(() => undefined);
}

function isInvitationForThisJoin(
  invitation: MeetingInvitation,
  command: JoinMeetingCommand,
  meetingId: string,
): boolean {
  const snapshot = invitation.toSnapshot();
  if (snapshot.meetingId !== meetingId) return false;
  if (snapshot.inviteeUserId !== null && snapshot.inviteeUserId !== command.user.userId) return false;
  return true;
}
