import type { PrismaClient } from '@prisma/client';
import type { AppointmentRoomLock } from '../../application/ports/appointment-room-lock.js';

const LOCK_TIMEOUT_MS = 10_000;

/**
 * Candado de SQL Server por cita (`sp_getapplock`, dueño = la transacción): se libera solo al terminar
 * o fallar. El trabajo corre en otras conexiones y confirma antes de soltarlo, así que quien espera ve
 * la sala ya creada.
 */
export class PrismaAppointmentRoomLock implements AppointmentRoomLock {
  constructor(private readonly prisma: PrismaClient) {}

  run<T>(tenantId: string, appointmentId: string, work: () => Promise<T>): Promise<T> {
    const resource = `comm-appointment-room:${tenantId}:${appointmentId}`;
    return this.prisma.$transaction(
      async (tx) => {
        const [acquired] = await tx.$queryRaw<Array<{ result: number }>>`
          DECLARE @result int;
          EXEC @result = sp_getapplock @Resource = ${resource}, @LockMode = 'Exclusive',
            @LockOwner = 'Transaction', @LockTimeout = ${LOCK_TIMEOUT_MS};
          SELECT @result AS result;`;
        // < 0: no se obtuvo (timeout o deadlock); el reintento de la cola lo vuelve a intentar.
        if (!acquired || acquired.result < 0) {
          throw new Error(`Appointment room lock not acquired (${acquired?.result ?? 'none'}).`);
        }
        return work();
      },
      { maxWait: LOCK_TIMEOUT_MS, timeout: LOCK_TIMEOUT_MS * 3 },
    );
  }
}
