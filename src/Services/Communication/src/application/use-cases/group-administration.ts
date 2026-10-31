import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { Conversation, GroupRemovalOutcome } from '../../domain/conversations/conversation.js';
import type { GroupRole } from '../../domain/conversations/conversation-participant.js';
import { ConversationKind } from '../../domain/conversations/conversation-kind.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { ConversationAuditEntry, ConversationAuditRepository } from '../ports/conversation-audit-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { NotificationRepository } from '../ports/notification-repository.js';
import type { CloudStorageMetadataClient } from '../ports/cloudstorage-metadata-client.js';
import {
  ChatSocketEvents,
  type ConversationParticipantRemovedDto,
  type ConversationParticipantRoleChangedDto,
  type ConversationUpdatedDto,
} from '../../contracts/socket/chat-socket-events.js';
import type { SocketEnvelope } from '../../contracts/socket/socket-envelope.js';
import { messageSnapshotToDto } from './chat-mappers.js';
import { pushAndEmitNotification } from './push-notification.js';
import { isTenantAdminActor } from './group-membership-policy.js';
import { logger } from '../../infrastructure/logger/logger.js';

/**
 * F8b: administración de grupos (D-22, D-23, T-21, T-22). Las reglas viven en el aggregate; aquí solo se
 * orquesta el efecto común de cada acción: mensaje `System` en el hilo, guardar, fila de auditoría y aviso en
 * vivo. No se publican eventos de integración nuevos: ningún servicio los consume (la auditoría es la tabla).
 */
export interface GroupActor {
  readonly userId: string;
  readonly displayName: string;
  readonly actorType: string;
  /**
   * D-22: superpoder para administrar cualquier grupo de la oficina sin ser miembro (Tenant Admin o permiso
   * `communication.group.manage_members`). Ausente = se decide solo por el tipo de actor.
   */
  readonly canAdministerAnyGroup?: boolean;
}

export function actorHasGroupSuperpower(actor: GroupActor): boolean {
  return actor.canAdministerAnyGroup ?? isTenantAdminActor(actor.actorType);
}

export interface GroupAdminDeps {
  readonly conversations: ConversationRepository;
  readonly conversationAudit?: ConversationAuditRepository | undefined;
  readonly emitter?: RealtimeEmitter | undefined;
}

type AuditDraft = Omit<ConversationAuditEntry, 'tenantId' | 'conversationId' | 'occurredAtUtc'>;

export interface GroupChange {
  readonly systemText?: string | null;
  readonly audit: readonly AuditDraft[];
  readonly updated?: Omit<ConversationUpdatedDto, 'conversationId' | 'kind'> | null;
  readonly roleChanges?: ReadonlyArray<{ userId: string; role: GroupRole }>;
  readonly changedByUserId?: string | null;
}

function envelope<T>(payload: T): SocketEnvelope<T> {
  return { eventId: randomUUID(), correlationId: '', emittedAtUtc: new Date().toISOString(), payload };
}

/** Efecto común de toda acción de administración: System + guardar + auditar + avisar a la sala. */
export async function commitGroupChange(
  conversation: Conversation,
  change: GroupChange,
  deps: GroupAdminDeps,
  now: Date = new Date(),
): Promise<void> {
  const system = change.systemText ? conversation.appendSystemMessage({ body: change.systemText, now }) : null;
  await deps.conversations.save(conversation);

  if (deps.conversationAudit && change.audit.length > 0) {
    // La acción ya quedó hecha: si la bitácora falla no se revierte, se deja rastro en el log.
    await deps.conversationAudit
      .append(change.audit.map((a) => ({ ...a, tenantId: conversation.tenantId, conversationId: conversation.id, occurredAtUtc: now })))
      .catch((err: unknown) => logger.error({ err, conversationId: conversation.id }, 'group audit: no se pudo registrar'));
  }

  const emitter = deps.emitter;
  if (!emitter) return;
  if (system?.isSuccess) {
    emitter.emitToConversation({
      tenantId: conversation.tenantId,
      conversationId: conversation.id,
      event: ChatSocketEvents.MessageNew,
      envelope: envelope({ ...messageSnapshotToDto(system.value.toSnapshot()), conversationKind: ConversationKind.Group, meetingId: null }),
    });
  }
  for (const role of change.roleChanges ?? []) {
    const dto: ConversationParticipantRoleChangedDto = {
      conversationId: conversation.id,
      userId: role.userId,
      role: role.role,
      changedByUserId: change.changedByUserId ?? null,
      changedAtUtc: now.toISOString(),
    };
    emitter.emitToConversation({
      tenantId: conversation.tenantId,
      conversationId: conversation.id,
      event: ChatSocketEvents.ConversationParticipantRoleChanged,
      envelope: envelope(dto),
    });
  }
  if (change.updated) {
    const dto: ConversationUpdatedDto = { conversationId: conversation.id, kind: conversation.kind, ...change.updated };
    emitter.emitToConversation({
      tenantId: conversation.tenantId,
      conversationId: conversation.id,
      event: ChatSocketEvents.ConversationUpdated,
      envelope: envelope(dto),
    });
  }
}

/** Nombre a mostrar en los mensajes System: el del participante; el Tenant Admin no miembro trae el suyo. */
export function nameOf(conversation: Conversation, userId: string, fallback = 'Someone'): string {
  return conversation.getParticipantSnapshots().find((p) => p.userId === userId)?.displayName ?? fallback;
}

/** Sucesión y archivado tras una salida, como partes del cambio (System + auditoría + avisos). */
export function removalSuccessionChange(
  conversation: Conversation,
  outcome: GroupRemovalOutcome,
): Pick<GroupChange, 'audit' | 'roleChanges' | 'updated'> & { extraText: string | null } {
  const audit: AuditDraft[] = [];
  const texts: string[] = [];
  if (outcome.newOwnerUserId) {
    texts.push(`${nameOf(conversation, outcome.newOwnerUserId)} is now the group owner`);
    audit.push({ action: 'OwnershipTransferred', actorUserId: null, targetUserId: outcome.newOwnerUserId, metadata: { reason: 'Succession' } });
  }
  if (outcome.archived) {
    texts.push('This group is now read-only: no office staff remain');
    audit.push({ action: 'Archived', actorUserId: null, targetUserId: null, metadata: { reason: 'NoStaffLeft' } });
  }
  return {
    audit,
    roleChanges: outcome.newOwnerUserId ? [{ userId: outcome.newOwnerUserId, role: 'Owner' }] : [],
    updated: outcome.archived ? { isArchived: true } : null,
    extraText: texts.length > 0 ? texts.join('. ') : null,
  };
}

async function loadGroup(deps: GroupAdminDeps, tenantId: string, conversationId: string): Promise<Result<Conversation>> {
  const conversation = await deps.conversations.findById(tenantId, conversationId, 0);
  if (!conversation) return Result.fail(makeError('Chat.Conversation.NotFound', 'Conversation not found.'));
  if (conversation.kind !== ConversationKind.Group) {
    return Result.fail(makeError('Chat.Conversation.NotGroup', 'Only groups support this action.'));
  }
  return Result.ok(conversation);
}

export interface GroupCommandBase {
  readonly tenantId: string;
  readonly conversationId: string;
  readonly actor: GroupActor;
}

/** D-22: solo el Owner (o Tenant Admin) nombra o quita Admins. */
export async function setGroupMemberRole(
  cmd: GroupCommandBase & { targetUserId: string; role: 'Admin' | 'Member' },
  deps: GroupAdminDeps,
): Promise<Result<{ userId: string; role: 'Admin' | 'Member' }>> {
  const loaded = await loadGroup(deps, cmd.tenantId, cmd.conversationId);
  if (!loaded.isSuccess) return Result.fail(loaded.error);
  const conversation = loaded.value;
  const before = conversation.roleOf(cmd.targetUserId);
  const now = new Date();
  const result = conversation.setMemberRole({
    actorUserId: cmd.actor.userId,
    targetUserId: cmd.targetUserId,
    role: cmd.role,
    actorIsTenantAdmin: actorHasGroupSuperpower(cmd.actor),
    now,
  });
  if (!result.isSuccess) return Result.fail(result.error);
  if (before === cmd.role) return Result.ok({ userId: cmd.targetUserId, role: cmd.role });

  const target = nameOf(conversation, cmd.targetUserId);
  await commitGroupChange(
    conversation,
    {
      systemText: cmd.role === 'Admin' ? `${cmd.actor.displayName} made ${target} an admin` : `${cmd.actor.displayName} removed ${target} as admin`,
      audit: [{ action: 'RoleChanged', actorUserId: cmd.actor.userId, targetUserId: cmd.targetUserId, metadata: { from: before, to: cmd.role } }],
      roleChanges: [{ userId: cmd.targetUserId, role: cmd.role }],
      changedByUserId: cmd.actor.userId,
    },
    deps,
    now,
  );
  return Result.ok({ userId: cmd.targetUserId, role: cmd.role });
}

/** D-22: el Owner pasa la propiedad; queda como Admin. */
export async function transferGroupOwnership(
  cmd: GroupCommandBase & { targetUserId: string },
  deps: GroupAdminDeps,
): Promise<Result<{ newOwnerUserId: string; previousOwnerUserId: string | null }>> {
  const loaded = await loadGroup(deps, cmd.tenantId, cmd.conversationId);
  if (!loaded.isSuccess) return Result.fail(loaded.error);
  const conversation = loaded.value;
  const now = new Date();
  const result = conversation.transferOwnership({
    actorUserId: cmd.actor.userId,
    targetUserId: cmd.targetUserId,
    actorIsTenantAdmin: actorHasGroupSuperpower(cmd.actor),
    now,
  });
  if (!result.isSuccess) return Result.fail(result.error);
  const previous = result.value.previousOwnerUserId;
  if (previous === cmd.targetUserId) return Result.ok({ newOwnerUserId: cmd.targetUserId, previousOwnerUserId: previous });

  await commitGroupChange(
    conversation,
    {
      systemText: `${cmd.actor.displayName} made ${nameOf(conversation, cmd.targetUserId)} the group owner`,
      audit: [{ action: 'OwnershipTransferred', actorUserId: cmd.actor.userId, targetUserId: cmd.targetUserId, metadata: { previousOwnerUserId: previous } }],
      roleChanges: [
        ...(previous ? [{ userId: previous, role: 'Admin' as const }] : []),
        { userId: cmd.targetUserId, role: 'Owner' as const },
      ],
      changedByUserId: cmd.actor.userId,
    },
    deps,
    now,
  );
  return Result.ok({ newOwnerUserId: cmd.targetUserId, previousOwnerUserId: previous });
}

/** T-21: nombre, descripción e imagen. La imagen debe ser un archivo de esta oficina (CloudStorage). */
export async function updateGroupInfo(
  cmd: GroupCommandBase & { title?: string; description?: string | null; avatarFileId?: string | null },
  deps: GroupAdminDeps & { cloudStorageMetadata?: CloudStorageMetadataClient | undefined },
): Promise<Result<{ title: string | null; description: string | null; avatarFileId: string | null }>> {
  const loaded = await loadGroup(deps, cmd.tenantId, cmd.conversationId);
  if (!loaded.isSuccess) return Result.fail(loaded.error);
  const conversation = loaded.value;

  if (cmd.avatarFileId && deps.cloudStorageMetadata) {
    const metadata = await deps.cloudStorageMetadata.getMetadata(cmd.tenantId, cmd.avatarFileId).catch(() => null);
    if (!metadata) return Result.fail(makeError('Chat.Group.AvatarNotFound', 'The group image was not found.'));
  }

  const before = { title: conversation.title, description: conversation.description, avatarFileId: conversation.avatarFileId };
  const now = new Date();
  const result = conversation.updateGroupInfo({
    actorUserId: cmd.actor.userId,
    ...(cmd.title !== undefined ? { title: cmd.title } : {}),
    ...(cmd.description !== undefined ? { description: cmd.description } : {}),
    ...(cmd.avatarFileId !== undefined ? { avatarFileId: cmd.avatarFileId } : {}),
    actorIsTenantAdmin: actorHasGroupSuperpower(cmd.actor),
    now,
  });
  if (!result.isSuccess) return Result.fail(result.error);

  const after = { title: conversation.title, description: conversation.description, avatarFileId: conversation.avatarFileId };
  const changed = (Object.keys(after) as (keyof typeof after)[]).filter((k) => after[k] !== before[k]);
  if (changed.length > 0) {
    const texts: string[] = [];
    if (result.value.titleChanged) texts.push(`${cmd.actor.displayName} renamed the group to "${after.title}"`);
    if (after.description !== before.description) texts.push(`${cmd.actor.displayName} updated the group description`);
    if (after.avatarFileId !== before.avatarFileId) texts.push(`${cmd.actor.displayName} changed the group photo`);
    await commitGroupChange(
      conversation,
      {
        systemText: texts.join('. '),
        audit: [{ action: 'InfoUpdated', actorUserId: cmd.actor.userId, targetUserId: null, metadata: { changed, previousTitle: before.title } }],
        updated: Object.fromEntries(changed.map((k) => [k, after[k]])),
      },
      deps,
      now,
    );
  }
  return Result.ok(after);
}

/** D-23: modo anuncio. */
export async function setGroupAdminsOnly(
  cmd: GroupCommandBase & { enabled: boolean },
  deps: GroupAdminDeps,
): Promise<Result<{ adminsOnlyWrite: boolean }>> {
  const loaded = await loadGroup(deps, cmd.tenantId, cmd.conversationId);
  if (!loaded.isSuccess) return Result.fail(loaded.error);
  const conversation = loaded.value;
  const now = new Date();
  const result = conversation.setAdminsOnlyWrite({
    actorUserId: cmd.actor.userId,
    enabled: cmd.enabled,
    actorIsTenantAdmin: actorHasGroupSuperpower(cmd.actor),
    now,
  });
  if (!result.isSuccess) return Result.fail(result.error);
  if (result.value.changed) {
    await commitGroupChange(
      conversation,
      {
        systemText: cmd.enabled
          ? `${cmd.actor.displayName} changed the group so only admins can send messages`
          : `${cmd.actor.displayName} changed the group so all members can send messages`,
        audit: [{ action: 'AdminsOnlyChanged', actorUserId: cmd.actor.userId, targetUserId: null, metadata: { enabled: cmd.enabled } }],
        updated: { adminsOnlyWrite: cmd.enabled },
      },
      deps,
      now,
    );
  }
  return Result.ok({ adminsOnlyWrite: cmd.enabled });
}

/** ADD-05 H-6: silencio de notificaciones propio (cualquier tipo con participantes). Sin auditoría ni System. */
export async function setConversationMuted(
  cmd: { tenantId: string; conversationId: string; userId: string; muted: boolean },
  deps: Pick<GroupAdminDeps, 'conversations'>,
): Promise<Result<{ conversationId: string; kind: Conversation['kind']; isMuted: boolean }>> {
  const conversation = await deps.conversations.findById(cmd.tenantId, cmd.conversationId, 0);
  if (!conversation) return Result.fail(makeError('Chat.Conversation.NotFound', 'Conversation not found.'));
  const result = conversation.setMuted(cmd.userId, cmd.muted);
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.conversations.save(conversation);
  return Result.ok({ conversationId: conversation.id, kind: conversation.kind, isMuted: cmd.muted });
}

/**
 * T-19: una cuenta dada de baja (desactivada u offboarding) sale de todos sus grupos sin actor, con la misma
 * sucesión y archivado que una salida normal; sus sockets dejan las salas.
 */
export async function removeUserFromGroups(
  cmd: { tenantId: string; userId: string; reason: 'Deactivated' | 'Offboarded' },
  deps: GroupAdminDeps,
): Promise<number> {
  const snapshots = await deps.conversations.listForUser({
    tenantId: cmd.tenantId,
    userId: cmd.userId,
    take: 500,
    skip: 0,
    kinds: [ConversationKind.Group],
  });
  let removed = 0;
  for (const snapshot of snapshots) {
    const conversation = await deps.conversations.findById(cmd.tenantId, snapshot.id, 0);
    if (!conversation) continue;
    const name = nameOf(conversation, cmd.userId);
    const now = new Date();
    const outcome = conversation.removeForDeactivation(cmd.userId, now);
    if (!outcome) continue;
    const succession = removalSuccessionChange(conversation, outcome);
    await commitGroupChange(
      conversation,
      {
        systemText: [`${name} is no longer with the office`, succession.extraText].filter(Boolean).join('. '),
        audit: [{ action: 'MemberRemoved', actorUserId: null, targetUserId: cmd.userId, metadata: { reason: cmd.reason } }, ...succession.audit],
        roleChanges: succession.roleChanges ?? [],
        updated: succession.updated ?? null,
        changedByUserId: null,
      },
      deps,
      now,
    );
    if (deps.emitter) {
      const dto: ConversationParticipantRemovedDto = {
        conversationId: conversation.id,
        removedByUserId: cmd.userId,
        removedParticipantUserId: cmd.userId,
        reason: 'Kicked',
        removedAtUtc: now.toISOString(),
      };
      deps.emitter.emitToConversation({
        tenantId: cmd.tenantId,
        conversationId: conversation.id,
        event: ChatSocketEvents.ConversationParticipantRemoved,
        envelope: envelope(dto),
      });
      deps.emitter.removeUserFromConversation?.({ tenantId: cmd.tenantId, userId: cmd.userId, conversationId: conversation.id });
    }
    removed++;
  }
  return removed;
}

/** D-21: aviso "X added you to {group}" en la campana del nuevo miembro (idempotente por evento). */
export async function notifyAddedToGroup(
  input: {
    tenantId: string;
    sourceEventId: string;
    correlationId: string;
    conversationId: string;
    groupTitle: string | null;
    addedUserId: string;
    actor: GroupActor;
  },
  deps: { notifications?: NotificationRepository | undefined; emitter?: RealtimeEmitter | undefined },
): Promise<void> {
  if (!deps.notifications || !deps.emitter || input.addedUserId === input.actor.userId) return;
  const group = input.groupTitle ?? 'a group';
  await pushAndEmitNotification(
    {
      eventId: input.sourceEventId,
      eventType: 'communication.chat.conversation_participant_added.v1',
      tenantId: input.tenantId,
      correlationId: input.correlationId,
      occurredOnUtc: new Date().toISOString(),
      payload: {},
    },
    {
      userId: input.addedUserId,
      kind: 'communication.chat.group_member_added',
      priority: 'Normal',
      title: `Added to ${group}`,
      body: `${input.actor.displayName} added you to ${group}.`,
      metadata: { conversationId: input.conversationId },
      category: 'Work',
      actorUserId: input.actor.userId,
      actorDisplayName: input.actor.displayName,
      actorType: input.actor.actorType === 'TenantAdmin' ? 'TenantAdmin' : 'TenantEmployee',
      resourceType: 'conversation',
      resourceId: input.conversationId,
      action: 'open',
      groupKey: `group-added:${input.conversationId}`,
    },
    { notifications: deps.notifications, emitter: deps.emitter },
  ).catch((err: unknown) => logger.warn({ err, conversationId: input.conversationId }, 'group added: no se pudo notificar'));
}
