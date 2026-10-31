/**
 * F11 (seguridad, E-259): quién inició la subida mediada de un adjunto y para qué conversación.
 * `send-message` exige la reserva y la consume: el `attachmentFileId` que manda el cliente ya no se
 * puede tomar de otro chat (ni de otra oficina) ni usarse dos veces.
 */
export interface AttachmentUploadReservationRepository {
  reserve(input: { fileId: string; tenantId: string; conversationId: string; uploadedByUserId: string }): Promise<void>;
  /** Consume la reserva solo si coincide en todo; true si existía y se consumió (atómico). */
  consume(input: { fileId: string; tenantId: string; conversationId: string; uploadedByUserId: string }): Promise<boolean>;
  /** Limpieza de subidas que nunca se enviaron. */
  purgeOlderThan(cutoff: Date): Promise<number>;
}
