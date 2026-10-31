import { logger } from '../logger/logger.js';
import { sfuTransportFailuresTotal, sfuTransportsConnectedTotal } from '../telemetry/metrics.js';
import type { SfuObserver, SfuTransportStateChange } from '../../application/ports/sfu-service.js';
import { isTransportFailure } from '../../application/use-cases/report-sfu-transport-state.js';

/**
 * Observador de infraestructura: log estructurado + metricas por cada transicion ICE/DTLS, y
 * delega al reporter de aplicacion (eventos de integracion). Un solo punto de cableado en main.ts.
 */
export function buildSfuObserver(reporter: SfuObserver): SfuObserver {
  return {
    onTransportStateChanged(change: SfuTransportStateChange): void {
      const failure = isTransportFailure(change);
      const log = failure ? logger.warn.bind(logger) : logger.info.bind(logger);
      log(
        {
          meetingId: change.meetingId,
          userId: change.userId,
          direction: change.direction,
          transportId: change.transportId,
          layer: change.layer,
          state: change.state,
          ...(change.selectedProtocol ? { protocol: change.selectedProtocol } : {}),
        },
        failure ? 'sfu transport failed' : 'sfu transport state',
      );
      if (failure) {
        sfuTransportFailuresTotal.inc({ direction: change.direction, layer: change.layer });
      } else if (change.layer === 'ice' && (change.state === 'connected' || change.state === 'completed')) {
        sfuTransportsConnectedTotal.inc({ direction: change.direction, protocol: change.selectedProtocol ?? 'unknown' });
      }
      reporter.onTransportStateChanged(change);
    },
    // F4: scores, capas y stats se delegan tal cual al reporter de aplicacion.
    ...(reporter.onProducerScore ? { onProducerScore: reporter.onProducerScore.bind(reporter) } : {}),
    ...(reporter.onConsumerScore ? { onConsumerScore: reporter.onConsumerScore.bind(reporter) } : {}),
    ...(reporter.onConsumerLayersChanged ? { onConsumerLayersChanged: reporter.onConsumerLayersChanged.bind(reporter) } : {}),
    ...(reporter.onTransportStats ? { onTransportStats: reporter.onTransportStats.bind(reporter) } : {}),
  };
}
