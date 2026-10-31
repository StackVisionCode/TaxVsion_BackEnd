/**
 * F6 (INV-09 H-5): cierra todas las conexiones socket de un usuario de una oficina. Lo usa la
 * revocacion de una invitacion cuyo guest sigue dentro: primero la salida forzada (dominio), despues
 * el corte del transporte para que el ticket que aun tiene en la mano no sirva para seguir en la room.
 * Opcional en el contenedor: se cablea tarde, como el emitter, porque necesita el servidor Socket.IO.
 */
export interface SocketSessionTerminator {
  disconnectUser(tenantId: string, userId: string): Promise<void>;
}
