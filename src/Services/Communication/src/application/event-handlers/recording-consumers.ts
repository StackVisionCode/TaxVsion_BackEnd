import type { IncomingEnvelope } from '../ports/event-consumer.js';
import {
  RecorderEventTypes,
  RecordingComposedPayloadSchema,
  RecordingCompositionFailedPayloadSchema,
} from '../../contracts/events/recording-events.js';
import {
  handleCompositionFailed,
  handleRecordingComposed,
  type RecordingLifecycleDeps,
} from '../use-cases/recording-lifecycle.js';
import { logger } from '../../infrastructure/logger/logger.js';

/**
 * F7 (contrato 9.2): resultados del recorder. Payload validado con zod — llega de otro proceso y un
 * campo mal formado se descarta con log en vez de dejar la sesion a medias.
 */
export function bindRecordingConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: RecordingLifecycleDeps,
): void {
  register(RecorderEventTypes.Composed, async (env) => {
    const parsed = RecordingComposedPayloadSchema.safeParse(env.payload);
    if (!parsed.success) {
      logger.warn({ issues: parsed.error.issues.slice(0, 5) }, 'invalid recording.composed payload');
      return;
    }
    await handleRecordingComposed(parsed.data, { tenantId: env.tenantId, correlationId: env.correlationId ?? null }, deps);
  });

  register(RecorderEventTypes.CompositionFailed, async (env) => {
    const parsed = RecordingCompositionFailedPayloadSchema.safeParse(env.payload);
    if (!parsed.success) {
      logger.warn({ issues: parsed.error.issues.slice(0, 5) }, 'invalid recording.composition_failed payload');
      return;
    }
    await handleCompositionFailed(parsed.data, { tenantId: env.tenantId, correlationId: env.correlationId ?? null }, deps);
  });
}
