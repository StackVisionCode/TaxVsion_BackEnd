import type { FastifyInstance } from 'fastify';
import { z } from 'zod';
import { checkPermission, permissionCheckHttpStatus, CommunicationPermissions } from '../../../domain/shared/permissions.js';
import { getOrCreateSettings, updateSettings } from '../../../application/use-cases/settings-use-cases.js';
import type { AppContainer } from '../../../infrastructure/container.js';

const PatchBody = z.object({
  chatEnabled: z.boolean().optional(),
  callsEnabled: z.boolean().optional(),
  videoCallsEnabled: z.boolean().optional(),
  meetingsEnabled: z.boolean().optional(),
  supportEnabled: z.boolean().optional(),
  screenshotsEnabled: z.boolean().optional(),
  internalGroupsEnabled: z.boolean().optional(),
  employeeToEmployeeChatEnabled: z.boolean().optional(),
  restrictCustomerChatToAssignedPreparer: z.boolean().optional(),
  defaultCameraOff: z.boolean().optional(),
  defaultMicrophoneOff: z.boolean().optional(),
  persistChatOnEnd: z.boolean().optional(),
  messageRetentionDays: z.number().int().min(1).max(3650).optional(),
  recordingRetentionDays: z.number().int().min(1).max(3650).optional(),
  purgeEnabled: z.boolean().optional(),
});

export async function registerSettingsRoutes(app: FastifyInstance, container: AppContainer): Promise<void> {
  app.get('/communication/settings', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const permCheck = await checkPermission(principal, CommunicationPermissions.SettingsManage, container.userPermissions);
    if (!permCheck.allowed) {
      return reply.code(permissionCheckHttpStatus(permCheck)).send({ code: permCheck.code, message: permCheck.message });
    }
    const [settings, limits] = await Promise.all([
      getOrCreateSettings(principal.tenantId, container),
      container.limits.findByTenantId(principal.tenantId),
    ]);
    return reply.send({ settings: settings.toSnapshot(), limits });
  });

  app.put('/communication/settings', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const permCheck = await checkPermission(principal, CommunicationPermissions.SettingsManage, container.userPermissions);
    if (!permCheck.allowed) {
      return reply.code(permissionCheckHttpStatus(permCheck)).send({ code: permCheck.code, message: permCheck.message });
    }
    const body = PatchBody.parse(request.body);
    const patch = Object.fromEntries(Object.entries(body).filter(([, v]) => v !== undefined)) as Parameters<typeof updateSettings>[0]['patch'];
    const result = await updateSettings({ tenantId: principal.tenantId, patch }, container);
    if (!result.isSuccess) {
      return reply.code(400).send({ code: result.error.code, message: result.error.message });
    }
    return reply.send(result.value);
  });
}
