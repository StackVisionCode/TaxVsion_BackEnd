import type { FastifyInstance } from 'fastify';
import { z } from 'zod';
import type { AppContainer } from '../../../infrastructure/container.js';
import { isPlatformAdmin } from '../../../domain/shared/permissions.js';

const CreateBody = z.object({
  eventKey: z.string().min(1).max(100),
  audienceRole: z.string().min(1).max(50),
  actionType: z.enum(['DeepLink', 'None']),
  urlTemplate: z.string().min(1).max(500).nullable().optional(),
});

const UpdateBody = z.object({
  actionType: z.enum(['DeepLink', 'None']),
  urlTemplate: z.string().min(1).max(500).nullable().optional(),
});

/**
 * Admin CRUD de `NotificationActionMapping` — mismo espiritu que EventTemplateMapping de
 * Scribe (configurable en base de datos, sin tocar codigo). Config de plataforma, no de
 * tenant (ver docblock del modelo Prisma) — restringido a PlatformAdmin, no a
 * TenantAdmin via `communication.settings.manage` (esa es semanticamente por-tenant).
 */
export async function registerNotificationActionMappingRoutes(
  app: FastifyInstance,
  container: AppContainer,
): Promise<void> {
  app.get('/communication/admin/notification-action-mappings', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    if (!isPlatformAdmin(principal.actorType)) {
      return reply.code(403).send({ code: 'Auth.Forbidden', message: 'PlatformAdmin only.' });
    }
    const mappings = await container.notificationActionMappings.list();
    return reply.send({ mappings });
  });

  app.post('/communication/admin/notification-action-mappings', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    if (!isPlatformAdmin(principal.actorType)) {
      return reply.code(403).send({ code: 'Auth.Forbidden', message: 'PlatformAdmin only.' });
    }
    const body = CreateBody.parse(request.body);
    if (body.actionType === 'DeepLink' && !body.urlTemplate) {
      return reply
        .code(400)
        .send({ code: 'NotificationActionMapping.UrlTemplateRequired', message: 'urlTemplate is required when actionType is DeepLink.' });
    }
    const existing = await container.notificationActionMappings.findByEventKeyAndAudienceRole(
      body.eventKey,
      body.audienceRole,
    );
    if (existing) {
      return reply.code(409).send({
        code: 'NotificationActionMapping.AlreadyExists',
        message: `A mapping for (${body.eventKey}, ${body.audienceRole}) already exists.`,
      });
    }
    const created = await container.notificationActionMappings.create({
      eventKey: body.eventKey,
      audienceRole: body.audienceRole,
      actionType: body.actionType,
      urlTemplate: body.urlTemplate ?? null,
    });
    return reply.code(201).send(created);
  });

  app.put('/communication/admin/notification-action-mappings/:id', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    if (!isPlatformAdmin(principal.actorType)) {
      return reply.code(403).send({ code: 'Auth.Forbidden', message: 'PlatformAdmin only.' });
    }
    const { id } = request.params as { id: string };
    const body = UpdateBody.parse(request.body);
    if (body.actionType === 'DeepLink' && !body.urlTemplate) {
      return reply
        .code(400)
        .send({ code: 'NotificationActionMapping.UrlTemplateRequired', message: 'urlTemplate is required when actionType is DeepLink.' });
    }
    const updated = await container.notificationActionMappings.update(id, {
      actionType: body.actionType,
      urlTemplate: body.urlTemplate ?? null,
    });
    if (!updated) {
      return reply.code(404).send({ code: 'NotificationActionMapping.NotFound', message: 'Mapping not found.' });
    }
    return reply.send(updated);
  });
}
