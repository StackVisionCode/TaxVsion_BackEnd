/**
 * Billing realtime. El servicio Billing (.NET) avisa cuando cambia el estado de una factura (emisión, cobro, anulación,
 * edición o corrección manual); acá se relaya a la sala STAFF del tenant (`t:{tenantId}:staff`) para que la lista de
 * facturas de la oficina se actualice sin recargar y se avise "factura pagada". El payload es mínimo: el front vuelve a
 * pedir la factura por HTTP (no confía en lo que viaja por el socket).
 */

// ---------- Server -> Client ----------

export const BillingSocketEvents = {
  /** Cambió el estado de una factura; el front refresca esa fila (y muestra un aviso si quedó pagada). */
  InvoiceUpdated: 'invoice.updated',
} as const;

export interface InvoiceUpdatedPayload {
  invoiceId: string;
  invoiceNumber: string | null;
  fromStatus: string | null;
  status: string;
  /** Qué lo originó: Created / Issue / Payment / Void / Edit / ManualChange / StatusOverride. */
  trigger: string;
}
