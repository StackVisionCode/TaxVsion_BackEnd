import type { FastifyInstance } from 'fastify';
import { z } from 'zod';
import { checkPermission, permissionCheckHttpStatus, CommunicationPermissions } from '../../../domain/shared/permissions.js';
import { getSettingsOverview, updateSettings } from '../../../application/use-cases/settings-use-cases.js';
import { MEETING_SOUNDS_POLICIES } from '../../../domain/settings/technical-limits.js';
import type { AppContainer } from '../../../infrastructure/container.js';

const OptionalInt = z.number().int().nullable().optional();

/** Los rangos exactos los valida el dominio (técnico y techo); aquí solo la forma del JSON. */
const PatchBody = z.object({
  chatEnabled: z.boolean().optional(),
  callsEnabled: z.boolean().optional(),
  videoCallsEnabled: z.boolean().optional(),
  meetingsEnabled: z.boolean().optional(),
  supportEnabled: z.boolean().optional(),
  screenshotsEnabled: z.boolean().optional(),
  employeeToEmployeeChatEnabled: z.boolean().optional(),
  restrictCustomerChatToAssignedPreparer: z.boolean().optional(),
  groupsAllowMultipleClients: z.boolean().optional(),
  defaultCameraOff: z.boolean().optional(),
  defaultMicrophoneOff: z.boolean().optional(),
  persistChatOnEnd: z.boolean().optional(),
  // La retención (mensajes, grabaciones y purga) ya no la edita la oficina: la fija PlatformAdmin.
  recordingConsentPolicy: z.enum(['AllAcceptedRequired', 'NoRejections', 'HostOverride']).optional(),
  defaultMeetingDurationMinutes: OptionalInt,
  maxMeetingDurationMinutes: OptionalInt,
  maxMeetingParticipants: OptionalInt,
  maxRecordingMinutesMeeting: OptionalInt,
  maxRecordingMinutesAudioCall: OptionalInt,
  maxRecordingMinutesVideoCall: OptionalInt,
  ringingTimeoutSeconds: OptionalInt,
  meetingChatAttachmentsPerMinute: OptionalInt,
  // F8 (null = hereda la plataforma; nunca más que su techo).
  chatEditWindowMinutesDirect: OptionalInt,
  chatEditWindowMinutesGroup: OptionalInt,
  chatEditWindowMinutesSupport: OptionalInt,
  chatEditWindowMinutesMeeting: OptionalInt,
  chatMaxEditsPerMessage: OptionalInt,
  meetingChatAttachmentsPerMeeting: OptionalInt,
  meetingSoundsPolicy: z.enum(MEETING_SOUNDS_POLICIES).nullable().optional(),
  transcriptLanguage: z.string().min(2).max(16).nullable().optional(),
  recordingVisibleInPortal: z.boolean().optional(),
  guestsAllowed: z.boolean().optional(),
  // F4 (null = hereda la plataforma).
  clientsCanShareScreen: z.boolean().nullable().optional(),
  screenAudioAllowed: z.boolean().nullable().optional(),
  audioEchoCancellationDefault: z.boolean().nullable().optional(),
  audioNoiseSuppressionDefault: z.boolean().nullable().optional(),
  audioAutoGainControlDefault: z.boolean().nullable().optional(),
  echoMuteOnDetect: z.boolean().nullable().optional(),
  // F5 (D-50; null = hereda): apagar reacciones o acotar el catalogo.
  reactionsEnabled: z.boolean().nullable().optional(),
  meetingReactionCatalog: z.array(z.string().min(1).max(16)).max(30).nullable().optional(),
  // F6 (null = hereda).
  meetingReminderMinutesBefore: z.number().int().min(0).nullable().optional(),
  guestsWaitingRoom: z.boolean().nullable().optional(),
  // F7 (null = hereda): vocabulario del transcript y notas de voz transcritas.
  transcriptPrompt: z.string().max(1000).nullable().optional(),
  voiceNoteTranscriptEnabled: z.boolean().nullable().optional(),
});

export async function registerSettingsRoutes(app: FastifyInstance, container: AppContainer): Promise<void> {
  app.get('/communication/settings', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const permCheck = await checkPermission(principal, CommunicationPermissions.SettingsManage, container.userPermissions);
    if (!permCheck.allowed) {
      return reply.code(permissionCheckHttpStatus(permCheck)).send({ code: permCheck.code, message: permCheck.message });
    }
    return reply.send(await getSettingsOverview(principal.tenantId, container));
  });

  app.put('/communication/settings', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const permCheck = await checkPermission(principal, CommunicationPermissions.SettingsManage, container.userPermissions);
    if (!permCheck.allowed) {
      return reply.code(permissionCheckHttpStatus(permCheck)).send({ code: permCheck.code, message: permCheck.message });
    }
    const body = PatchBody.parse(request.body);
    const patch = Object.fromEntries(Object.entries(body).filter(([, v]) => v !== undefined)) as Parameters<typeof updateSettings>[0]['patch'];
    const result = await updateSettings(
      { tenantId: principal.tenantId, actorUserId: principal.userId, correlationId: request.id, patch },
      container,
    );
    if (!result.isSuccess) {
      return reply.code(400).send({ code: result.error.code, message: result.error.message });
    }
    return reply.send(result.value);
  });
}
