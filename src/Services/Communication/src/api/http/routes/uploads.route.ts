import type { FastifyInstance } from 'fastify';
import multipart from '@fastify/multipart';
import { uploadToCloudStorage } from '../../../infrastructure/http-clients/cloudstorage-client.js';

// Tope razonable para grabaciones de meeting/call via MediaRecorder — evita
// que una grabacion larga sin limite tumbe el proceso por memoria (el body
// entero se bufferiza antes de reenviarlo a CloudStorage).
const MAX_RECORDING_BYTES = 200 * 1024 * 1024;

export async function registerUploadRoutes(app: FastifyInstance): Promise<void> {
  await app.register(multipart, { limits: { fileSize: MAX_RECORDING_BYTES } });

  // Puente para que el navegador suba la grabacion de un meeting/call sin
  // hablar M2M con CloudStorage directamente. El frontend sube aca, este
  // endpoint reenvia via el flujo presignado (initiate -> MinIO -> complete)
  // y devuelve el fileId para `meeting.recording.attach` / `call.recording.attach`.
  app.post(
    '/communication/uploads/meeting-recording',
    { preHandler: [app.authenticate] },
    async (request, reply) => {
      const principal = request.principal!;

      // `request.file()` solo devuelve el PRIMER part y NO sigue drenando el
      // resto del stream — si el cliente manda el campo "meetingId" DESPUES
      // de "file" (como hace este frontend), `data.fields.meetingId` esta
      // vacio en ese momento y respondiamos 400 sin haber leido el resto del
      // body. Eso deja al gateway (YARP) escribiendo bytes contra una
      // conexion que el server ya cerro -> "connection forcibly closed" /
      // 502. Iterar TODOS los parts via `request.parts()` garantiza que el
      // stream entero se drena sin importar el orden de los campos.
      let fileName: string | undefined;
      let mimetype: string | undefined;
      let buffer: Buffer | undefined;
      let meetingId: string | undefined;

      try {
        for await (const part of request.parts()) {
          if (part.type === 'file') {
            fileName = part.filename;
            mimetype = part.mimetype;
            buffer = await part.toBuffer();
          } else if (part.fieldname === 'meetingId') {
            meetingId = String(part.value);
          }
        }
      } catch {
        return reply.code(413).send({ code: 'Upload.TooLarge', message: 'File exceeds the size limit.' });
      }

      if (!buffer || !fileName || !mimetype) {
        return reply.code(400).send({ code: 'Upload.NoFile', message: 'Multipart body must include a "file" field.' });
      }
      if (!meetingId) {
        return reply.code(400).send({ code: 'Upload.NoMeetingId', message: 'meetingId field is required.' });
      }

      try {
        const fileId = await uploadToCloudStorage({
          tenantId: principal.tenantId,
          fileName,
          contentType: mimetype,
          content: buffer,
          ownerType: 'Communication',
          ownerId: meetingId,
          folderType: 'Recordings',
        });
        return reply.code(201).send({ fileId });
      } catch (err) {
        request.log.error({ err }, 'CloudStorage upload failed');
        return reply
          .code(502)
          .send({ code: 'Upload.CloudStorageFailed', message: 'Could not upload the recording to storage.' });
      }
    },
  );
}
