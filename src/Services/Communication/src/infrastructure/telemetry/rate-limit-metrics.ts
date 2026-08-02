import { metrics, type Counter } from '@opentelemetry/api';

/**
 * RateLimit Fase 8 (Plan_Implementacion_Fases.md §8) — espejo de
 * BuildingBlocks.Infrastructure.RateLimiting.RateLimitMetrics (.NET): mismo Meter name
 * ("TaxVision.RateLimit") y mismos 3 nombres de instrumento, para que el exporter OTLP los
 * aterrice como la misma serie Prometheus cross-stack (dots -> underscores, mismo criterio que ya
 * aplica a las metricas .NET). Usa @opentelemetry/api (no prom-client) para viajar por el mismo
 * pipeline OTLP -> otel-collector -> Prometheus que las trazas de Communication ya usan — antes de
 * esta fase, Communication no exportaba metricas OTel en absoluto (solo prom-client via /metrics,
 * que nada scrapea).
 *
 * Los Counter no se crean a nivel de modulo: `metrics.getMeter()` devuelve el Meter del
 * MeterProvider global vigente EN EL MOMENTO DE LA LLAMADA, y `startTelemetry()` (que registra el
 * MeterProvider real) corre despues de que ESM ya evaluo el grafo de imports de `container.ts`
 * (que importa este modulo). Crear los Counter en carga de modulo los dejaria pegados para
 * siempre al Meter no-op. Por eso se resuelven en el primer uso real (bien despues del boot).
 */
const METER_NAME = 'TaxVision.RateLimit';

let counters:
  | { evaluated: Counter; blocked: Counter; fallbackOpen: Counter }
  | undefined;

function getCounters() {
  if (!counters) {
    const meter = metrics.getMeter(METER_NAME);
    counters = {
      evaluated: meter.createCounter('ratelimit.evaluated_total', {
        description: 'Rate limit checks performed, by layer',
      }),
      blocked: meter.createCounter('ratelimit.blocked_total', {
        description: 'Requests rejected with 429, by layer',
      }),
      fallbackOpen: meter.createCounter('ratelimit.fallback_open_total', {
        description: 'Redis failures on the rate-limit path (Communication es fail-closed hoy — ver doc-comment de los limiters)',
      }),
    };
  }
  return counters;
}

export function recordEvaluated(policy: string, layer: string, tenantId: string): void {
  getCounters().evaluated.add(1, { policy, layer, tenant_id: tenantId });
}

export function recordBlocked(policy: string, layer: string, tenantId: string): void {
  getCounters().blocked.add(1, { policy, layer, tenant_id: tenantId });
}

export function recordFallbackOpen(policy: string, reason: string): void {
  getCounters().fallbackOpen.add(1, { policy, reason });
}
