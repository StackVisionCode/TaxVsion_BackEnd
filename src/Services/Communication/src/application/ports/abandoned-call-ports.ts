/** Sockets conectados a la room de una llamada (`t:{tenant}:call:{id}`), contando todas las instancias. */
export interface CallRoomPresence {
  connectedCount(tenantId: string, callId: string): Promise<number>;
}

/**
 * Desde cuándo una llamada abierta está sin nadie conectado. Compartido entre instancias: el barrido corre
 * donde tome el lock y no puede perder la cuenta al cambiar de instancia.
 */
export interface CallEmptinessClock {
  /** Primer instante (ms) en que se vio vacía; lo fija si es la primera vez. */
  emptySince(callId: string, nowMs: number): Promise<number>;
  clear(callId: string): Promise<void>;
}
