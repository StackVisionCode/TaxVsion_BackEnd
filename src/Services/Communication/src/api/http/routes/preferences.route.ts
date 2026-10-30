import type { FastifyInstance } from 'fastify';
import { z } from 'zod';
import { getUserPreferences, updateUserPreferences } from '../../../application/use-cases/user-preferences-use-cases.js';
import type { AppContainer } from '../../../infrastructure/container.js';

const PatchBody = z.object({
  meetingSoundsMuted: z.boolean().optional(),
  transcriptLanguage: z.string().min(2).max(16).nullable().optional(),
  preferAudioOnly: z.boolean().optional(),
  echoCancellation: z.boolean().nullable().optional(),
  noiseSuppression: z.boolean().nullable().optional(),
  autoGainControl: z.boolean().nullable().optional(),
  joinMuted: z.boolean().optional(),
  joinCameraOff: z.boolean().optional(),
});

/** Autoservicio (T-47): cualquier principal autenticado (staff o portal) edita solo las suyas; sin permiso extra. */
export async function registerPreferencesRoutes(app: FastifyInstance, container: AppContainer): Promise<void> {
  app.get('/communication/preferences', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const [preferences, effective] = await Promise.all([
      getUserPreferences({ tenantId: principal.tenantId, userId: principal.userId }, container),
      container.effectiveSettings.resolveForUser(principal.tenantId, principal.userId),
    ]);
    return reply.send({ preferences, effective: effective.user, soundsPolicy: effective.meeting.soundsPolicy });
  });

  app.put('/communication/preferences', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const body = PatchBody.parse(request.body);
    const patch = Object.fromEntries(Object.entries(body).filter(([, v]) => v !== undefined)) as Parameters<typeof updateUserPreferences>[0]['patch'];
    const result = await updateUserPreferences(
      { tenantId: principal.tenantId, userId: principal.userId, correlationId: request.id, patch },
      container,
    );
    if (!result.isSuccess) return reply.code(400).send({ code: result.error.code, message: result.error.message });
    return reply.send({ preferences: result.value });
  });
}
