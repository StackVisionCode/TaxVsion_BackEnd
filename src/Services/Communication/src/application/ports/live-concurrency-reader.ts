/**
 * F10 (ADD-08 T-74): concurrencia en este instante (reuniones en vivo y participantes conectados).
 * Es una lectura puntual por índice, sin PII: alimenta los picos del snapshot y `admin/analytics/live`.
 * Los reportes por rango nunca la usan (salen del snapshot).
 */
export interface LiveConcurrency {
  readonly meetings: number;
  readonly participants: number;
}

export interface LiveConcurrencyReader {
  countForTenant(tenantId: string): Promise<LiveConcurrency>;
  /** Toda la plataforma, con el desglose de las oficinas que tienen algo en vivo. */
  countPlatform(): Promise<LiveConcurrency & { readonly byTenant: ReadonlyArray<LiveConcurrency & { readonly tenantId: string }> }>;
}
