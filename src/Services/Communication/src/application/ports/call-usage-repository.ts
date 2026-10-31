/**
 * Minutos de llamada consumidos por oficina y mes (INV-12 H-3 / ADD-11 H-4): se acumulan al terminar
 * cada llamada y se comparan con `calls.maxMonthlyMinutes` del plan al iniciar la siguiente.
 */
export interface CallUsageRepository {
  /** Suma segundos al mes `yearMonth` (formato `YYYY-MM`, UTC). Idempotencia la da el llamador (una vez por fin de llamada). */
  addSeconds(input: { tenantId: string; yearMonth: string; seconds: number }): Promise<void>;
  getSeconds(input: { tenantId: string; yearMonth: string }): Promise<number>;
}

export function yearMonthUtc(date: Date): string {
  return `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, '0')}`;
}
