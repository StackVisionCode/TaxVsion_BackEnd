import { NodeSDK } from '@opentelemetry/sdk-node';
import { getNodeAutoInstrumentations } from '@opentelemetry/auto-instrumentations-node';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-http';
import { Resource } from '@opentelemetry/resources';
import { SEMRESATTRS_SERVICE_NAME } from '@opentelemetry/semantic-conventions';
import { config } from '../config.js';
import { logger } from '../logger/logger.js';

let sdk: NodeSDK | undefined;

/**
 * Inicializa OpenTelemetry. Si no hay endpoint configurado, no arranca — permite
 * ejecutar tests o dev local sin collector.
 */
export function startTelemetry(): void {
  if (!config.otel.endpoint) {
    logger.warn('OTEL endpoint not configured — telemetry disabled.');
    return;
  }

  sdk = new NodeSDK({
    resource: new Resource({
      [SEMRESATTRS_SERVICE_NAME]: config.otel.serviceName,
    }),
    traceExporter: new OTLPTraceExporter({
      url: `${config.otel.endpoint}/v1/traces`,
    }),
    instrumentations: [
      getNodeAutoInstrumentations({
        // fs es ruidoso y no aporta valor operacional.
        '@opentelemetry/instrumentation-fs': { enabled: false },
      }),
    ],
  });

  sdk.start();
  logger.info({ endpoint: config.otel.endpoint }, 'OpenTelemetry started');
}

export async function shutdownTelemetry(): Promise<void> {
  if (sdk) {
    await sdk.shutdown();
  }
}
