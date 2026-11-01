import { RecordingScope } from '../../../domain/recording/recording-session-state.js';
import { lifecycleFrom, recomposeRecording } from '../../../application/use-cases/recording-lifecycle.js';
import type { FastifyInstance } from 'fastify';
import { z } from 'zod';
import { CommunicationPermissions } from '../../../domain/shared/permissions.js';
import { requirePlatformPermission } from '../require-platform-permission.js';
import { MEETING_SOUNDS_POLICIES } from '../../../domain/settings/technical-limits.js';
import { ConfigurationAuditScope } from '../../../domain/settings/configuration-audit.js';
import { getPlatformSettings, updatePlatformSettings } from '../../../application/use-cases/platform-settings-use-cases.js';
import { getTenantConstraints, updateTenantConstraints } from '../../../application/use-cases/tenant-constraints-use-cases.js';
import { listConfigurationAudit } from '../../../application/use-cases/configuration-audit-queries.js';
import type { AppContainer } from '../../../infrastructure/container.js';

const PositiveInt = z.number().int().positive();
const OptionalPositiveInt = PositiveInt.nullable().optional();

const PlatformPatchBody = z.object({
  maxMeetingDurationMinutes: PositiveInt.optional(),
  defaultMeetingDurationMinutes: PositiveInt.optional(),
  defaultMaxParticipants: PositiveInt.optional(),
  meshMaxParticipants: PositiveInt.optional(),
  disconnectGraceSeconds: PositiveInt.optional(),
  scheduledMeetingExpiryHours: PositiveInt.optional(),
  emptyMeetingEndMinutes: PositiveInt.optional(),
  dominantSpeakerMaxPerSecond: PositiveInt.optional(),
  guestsAllowed: z.boolean().optional(),
  ringingTimeoutSeconds: PositiveInt.optional(),
  recordingConsentTimeoutMeetingSeconds: PositiveInt.optional(),
  recordingConsentTimeoutCallSeconds: PositiveInt.optional(),
  maxRecordingMinutesMeeting: PositiveInt.optional(),
  maxRecordingMinutesAudioCall: PositiveInt.optional(),
  maxRecordingMinutesVideoCall: PositiveInt.optional(),
  invitationMinTtlHours: PositiveInt.optional(),
  invitationExpiryGraceHours: z.number().int().min(0).optional(),
  internalGroupsEnabled: z.boolean().optional(),
  meetingChatAttachmentsPerMinute: PositiveInt.optional(),
  meetingChatAttachmentsPerMeeting: PositiveInt.optional(),
  maxReactionsPerMessage: PositiveInt.optional(),
  // F8: ventana de edición por tipo de chat (0 = no se edita) y tope de ediciones; el rango lo valida el dominio.
  chatEditWindowMinutesDirect: z.number().int().min(0).optional(),
  chatEditWindowMinutesGroup: z.number().int().min(0).optional(),
  chatEditWindowMinutesSupport: z.number().int().min(0).optional(),
  chatEditWindowMinutesMeeting: z.number().int().min(0).optional(),
  chatMaxEditsPerMessage: PositiveInt.optional(),
  meetingSoundsPolicy: z.enum(MEETING_SOUNDS_POLICIES).optional(),
  transcriptDefaultLanguage: z.string().min(2).max(16).optional(),
  outboxRetentionDays: PositiveInt.optional(),
  processedEventRetentionDays: PositiveInt.optional(),
  idempotencyRetentionDays: PositiveInt.optional(),
  // F4: politica de media (rangos exactos en el dominio).
  qualitySampleIntervalSeconds: z.number().int().min(0).optional(),
  qualityExcellentRttMs: z.number().int().min(0).optional(),
  qualityGoodRttMs: z.number().int().min(0).optional(),
  qualityUnstableRttMs: z.number().int().min(0).optional(),
  qualityExcellentLossPct: z.number().int().min(0).optional(),
  qualityGoodLossPct: z.number().int().min(0).optional(),
  qualityUnstableLossPct: z.number().int().min(0).optional(),
  qualityExcellentJitterMs: z.number().int().min(0).optional(),
  qualityGoodJitterMs: z.number().int().min(0).optional(),
  qualityUnstableJitterMs: z.number().int().min(0).optional(),
  qualityUpgradeSamples: z.number().int().min(0).optional(),
  qualityPoorSustainedSamples: z.number().int().min(0).optional(),
  qualityRecoverySamples: z.number().int().min(0).optional(),
  cameraMaxWidth: z.number().int().min(0).optional(),
  cameraMaxHeight: z.number().int().min(0).optional(),
  cameraMaxFps: z.number().int().min(0).optional(),
  meshCameraMaxWidth: z.number().int().min(0).optional(),
  meshCameraMaxHeight: z.number().int().min(0).optional(),
  meshCameraMaxFps: z.number().int().min(0).optional(),
  screenMaxWidth: z.number().int().min(0).optional(),
  screenMaxHeight: z.number().int().min(0).optional(),
  screenMaxFps: z.number().int().min(0).optional(),
  screenVideoMaxFps: z.number().int().min(0).optional(),
  screenBitrateKbps: z.number().int().min(0).optional(),
  screenHighBitrateKbps: z.number().int().min(0).optional(),
  audioMaxAverageBitrateKbps: z.number().int().min(0).optional(),
  muteReleasesHardwareMinutes: z.number().int().min(0).optional(),
  soundsVolumePct: z.number().int().min(0).optional(),
  soundsRepeatGapMs: z.number().int().min(0).optional(),
  sfuMaxIncomingBitrateKbps: z.number().int().min(0).optional(),
  sfuStatsSampleSeconds: z.number().int().min(0).optional(),
  cameraMaxBitrateKbps: z.number().int().min(0).optional(),
  clientRecordingAudioKbps: z.number().int().min(0).optional(),
  opusFecEnabled: z.boolean().optional(),
  opusDtxEnabled: z.boolean().optional(),
  audioEchoCancellationDefault: z.boolean().optional(),
  audioNoiseSuppressionDefault: z.boolean().optional(),
  audioAutoGainControlDefault: z.boolean().optional(),
  clientsCanShareScreen: z.boolean().optional(),
  screenAudioAllowed: z.boolean().optional(),
  echoDetectionEnabled: z.boolean().optional(),
  echoMuteOnDetect: z.boolean().optional(),
  // F5 (D-50): reacciones del meeting (rangos y catalogo validados en el dominio).
  reactionsEnabled: z.boolean().optional(),
  reactionDurationSeconds: z.number().int().min(0).optional(),
  reactionsPerUserPer10s: z.number().int().min(0).optional(),
  reactionsPerMeetingPerMinute: z.number().int().min(0).optional(),
  meetingReactionCatalog: z.array(z.string().min(1).max(16)).max(30).optional(),
  // F6: recordatorio (0 = apagado) y sala de espera para guests.
  meetingReminderMinutesBefore: z.number().int().min(0).optional(),
  guestsWaitingRoomDefault: z.boolean().optional(),
  // F7: grabación server-side, límites, composición, retención de pistas y notas de voz (rangos en el dominio).
  serverSideRecordingEnabled: z.boolean().optional(),
  recordingLimitWarningMinutes: z.number().int().min(0).optional(),
  recordingLimitToleranceSeconds: z.number().int().min(0).optional(),
  recordingCompositionWidth: z.number().int().min(0).optional(),
  recordingCompositionHeight: z.number().int().min(0).optional(),
  recordingCompositionFps: z.number().int().min(0).optional(),
  recordingCompositionVideoKbps: z.number().int().min(0).optional(),
  recordingCompositionAudioKbps: z.number().int().min(0).optional(),
  recordingCompositionFormat: z.enum(['mp4', 'webm']).optional(),
  recordingCaptureSpatialLayer: z.number().int().min(0).optional(),
  recordingTracksRetentionDays: z.number().int().min(0).optional(),
  voiceNoteTranscriptEnabled: z.boolean().optional(),
  voiceNoteTranscriptMaxSeconds: z.number().int().min(0).optional(),
  transcriptWorkerTimeoutFactorPct: z.number().int().min(0).optional(),
  recordingAvailabilityTimeoutMinutes: z.number().int().min(0).optional(),
  // F10 (ADD-08): zona de analítica (IANA) y retenciones; null apaga la purga del histórico.
  analyticsTimeZone: z.string().min(1).max(64).optional(),
  analyticsRetentionDays: z.number().int().min(0).optional(),
  callsRetentionDays: z.number().int().min(0).nullable().optional(),
  meetingsRetentionDays: z.number().int().min(0).nullable().optional(),
  invitationsRetentionDays: z.number().int().min(0).nullable().optional(),
  // Retención de mensajes y grabaciones y purga automática, para todas las oficinas.
  messageRetentionDays: z.number().int().min(0).optional(),
  recordingRetentionDays: z.number().int().min(0).optional(),
  purgeEnabled: z.boolean().optional(),
});

const ConstraintsPatchBody = z.object({
  maxMeetingParticipants: OptionalPositiveInt,
  maxMeetingDurationMinutes: OptionalPositiveInt,
  maxRecordingMinutesMeeting: OptionalPositiveInt,
  maxRecordingMinutesAudioCall: OptionalPositiveInt,
  maxRecordingMinutesVideoCall: OptionalPositiveInt,
  recordingAllowed: z.boolean().nullable().optional(),
  guestsAllowed: z.boolean().nullable().optional(),
  // Excepción de retención de esta oficina (null = vuelve a la global).
  messageRetentionDays: OptionalPositiveInt,
  recordingRetentionDays: OptionalPositiveInt,
  purgeEnabled: z.boolean().nullable().optional(),
  notes: z.string().max(512).nullable().optional(),
});

const TenantParams = z.object({ tenantId: z.string().uuid() });

const AuditQuery = z.object({
  tenantId: z.string().uuid().optional(),
  scope: z.enum([
    ConfigurationAuditScope.Platform,
    ConfigurationAuditScope.TenantConstraints,
    ConfigurationAuditScope.TenantSettings,
    ConfigurationAuditScope.UserPreferences,
  ]).optional(),
  skip: z.coerce.number().int().min(0).default(0),
  take: z.coerce.number().int().min(1).max(200).default(50),
});

type Defined<T> = { [K in keyof T]?: Exclude<T[K], undefined> };

/** Zod deja `undefined` en las claves ausentes; el dominio solo acepta claves presentes. */
function stripUndefined<T extends object>(body: T): Defined<T> {
  return Object.fromEntries(Object.entries(body).filter(([, v]) => v !== undefined)) as Defined<T>;
}

const RecomposeParams = z.object({ scope: z.enum(['Meeting', 'Call']), id: z.string().uuid() });

export async function registerAdminSettingsRoutes(app: FastifyInstance, container: AppContainer): Promise<void> {
  // F7 (contrato 9.6): recomponer una grabacion server-side desde las pistas del recorder (soporte).
  app.post('/communication/admin/recordings/:scope/:id/recompose', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal;
    if (!principal) return reply.code(401).send({ code: 'Auth.Unauthenticated', message: 'Authentication required.' });
    const denied = await requirePlatformPermission(principal, CommunicationPermissions.PlatformManage, container);
    if (denied) return reply.code(denied.status).send(denied.body);
    const params = RecomposeParams.parse(request.params);
    const scope = params.scope === 'Call' ? RecordingScope.Call : RecordingScope.Meeting;
    const session = await container.recordingSessions.findByScopeAnyTenant(scope, params.id);
    const lifecycle = lifecycleFrom({ ...container, emitter: container.emitter });
    if (!session || !lifecycle) return reply.code(404).send({ code: 'Recording.NotFound', message: 'Recording not found.' });
    const result = await recomposeRecording(
      { tenantId: session.toSnapshot().tenantId, scope, scopeId: params.id, correlationId: request.id },
      lifecycle,
    );
    if (!result.isSuccess) {
      const status = result.error.code === 'Recording.NotFound' ? 404 : result.error.code === 'Recording.RecorderUnavailable' ? 503 : 409;
      return reply.code(status).send({ code: result.error.code, message: result.error.message });
    }
    return reply.code(202).send();
  });

  app.get('/communication/admin/platform-settings', { preHandler: [app.authenticate] }, async (request, reply) => {
    const denied = await requirePlatformPermission(request.principal!, CommunicationPermissions.PlatformManage, container);
    if (denied) return reply.code(denied.status).send(denied.body);
    return reply.send(await getPlatformSettings(container));
  });

  app.put('/communication/admin/platform-settings', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const denied = await requirePlatformPermission(principal, CommunicationPermissions.PlatformManage, container);
    if (denied) return reply.code(denied.status).send(denied.body);
    const patch = stripUndefined(PlatformPatchBody.parse(request.body));
    const result = await updatePlatformSettings(
      { actorUserId: principal.userId, correlationId: request.id, patch },
      { ...container, emitter: container.emitter },
    );
    if (!result.isSuccess) return reply.code(400).send({ code: result.error.code, message: result.error.message });
    return reply.send(result.value);
  });

  app.get('/communication/admin/tenants/:tenantId/constraints', { preHandler: [app.authenticate] }, async (request, reply) => {
    const denied = await requirePlatformPermission(request.principal!, CommunicationPermissions.ConstraintsManage, container);
    if (denied) return reply.code(denied.status).send(denied.body);
    const params = TenantParams.parse(request.params);
    return reply.send(await getTenantConstraints(params.tenantId, container));
  });

  app.put('/communication/admin/tenants/:tenantId/constraints', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const denied = await requirePlatformPermission(principal, CommunicationPermissions.ConstraintsManage, container);
    if (denied) return reply.code(denied.status).send(denied.body);
    const params = TenantParams.parse(request.params);
    const patch = stripUndefined(ConstraintsPatchBody.parse(request.body));
    const result = await updateTenantConstraints(
      { tenantId: params.tenantId, actorUserId: principal.userId, correlationId: request.id, patch },
      { ...container, emitter: container.emitter },
    );
    if (!result.isSuccess) return reply.code(400).send({ code: result.error.code, message: result.error.message });
    return reply.send(result.value);
  });

  app.get('/communication/admin/audit', { preHandler: [app.authenticate] }, async (request, reply) => {
    const denied = await requirePlatformPermission(request.principal!, CommunicationPermissions.PlatformManage, container);
    if (denied) return reply.code(denied.status).send(denied.body);
    const query = AuditQuery.parse(request.query);
    return reply.send(
      await listConfigurationAudit(
        {
          ...(query.tenantId !== undefined ? { tenantId: query.tenantId } : {}),
          ...(query.scope !== undefined ? { scope: query.scope } : {}),
          skip: query.skip,
          take: query.take,
        },
        container,
      ),
    );
  });
}
