/**
 * Estados de una llamada 1:1. Transiciones validas (aplicadas por el aggregate):
 *
 *   Ringing -> Accepted -> Active -> Ended
 *              \-> Ended (early hangup del callee)
 *   Ringing -> Rejected
 *   Ringing -> Cancelled
 *   Ringing -> MissedCall (timeout server-side; publica CommunicationMissedCall)
 *   Ringing|Accepted|Active -> Failed (IceFailed, network, etc)
 */
export const CallStatus = {
  Ringing: 'Ringing',
  Accepted: 'Accepted',
  Active: 'Active',
  Ended: 'Ended',
  Rejected: 'Rejected',
  Cancelled: 'Cancelled',
  MissedCall: 'MissedCall',
  Failed: 'Failed',
} as const;

export type CallStatus = (typeof CallStatus)[keyof typeof CallStatus];

export function isCallStatus(value: string): value is CallStatus {
  return (
    value === 'Ringing' ||
    value === 'Accepted' ||
    value === 'Active' ||
    value === 'Ended' ||
    value === 'Rejected' ||
    value === 'Cancelled' ||
    value === 'MissedCall' ||
    value === 'Failed'
  );
}

/**
 * Estados terminales: no aceptan transiciones adicionales.
 */
export function isTerminal(status: CallStatus): boolean {
  return (
    status === 'Ended' ||
    status === 'Rejected' ||
    status === 'Cancelled' ||
    status === 'MissedCall' ||
    status === 'Failed'
  );
}
