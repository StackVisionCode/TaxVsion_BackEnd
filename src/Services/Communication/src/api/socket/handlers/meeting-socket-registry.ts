/**
 * Que socket representa a cada participante dentro de una reunion. La señalizacion mesh se dirige a
 * ese socket y no al room del usuario: con dos pestañas abiertas, solo la que esta en la reunion debe
 * recibir ofertas y candidatos (INV-02 H-9). Memoria del proceso, igual que las salidas en gracia.
 */
export class MeetingSocketRegistry {
  private readonly socketByParticipant = new Map<string, string>();

  register(tenantId: string, meetingId: string, userId: string, socketId: string): void {
    this.socketByParticipant.set(this.key(tenantId, meetingId, userId), socketId);
  }

  /** Solo borra si el socket que se va sigue siendo el vigente: un join desde otra pestaña ya lo reemplazo. */
  unregister(tenantId: string, meetingId: string, userId: string, socketId: string): void {
    const key = this.key(tenantId, meetingId, userId);
    if (this.socketByParticipant.get(key) === socketId) {
      this.socketByParticipant.delete(key);
    }
  }

  find(tenantId: string, meetingId: string, userId: string): string | undefined {
    return this.socketByParticipant.get(this.key(tenantId, meetingId, userId));
  }

  private key(tenantId: string, meetingId: string, userId: string): string {
    return `${tenantId}:${meetingId}:${userId}`;
  }
}
