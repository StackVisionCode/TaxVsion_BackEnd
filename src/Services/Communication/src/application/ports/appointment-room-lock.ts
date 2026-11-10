/**
 * Serializa el trabajo sobre la sala de una cita: el aviso de Calendar y su reparación pueden llegar a la
 * vez (prefetch en paralelo) y sin esto los dos verían que no hay sala y crearían dos.
 */
export interface AppointmentRoomLock {
  run<T>(tenantId: string, appointmentId: string, work: () => Promise<T>): Promise<T>;
}
