/**
 * Customer realtime (backlog 5.1). El directorio de clientes es COMPARTIDO por tenant, así que al
 * aplicar un `customer.*.v1` se avisa a todo el tenant (`t:{tenantId}`) y el cache de clientes del
 * front invalida ese cliente para revalidar al próximo uso. Payload mínimo — solo id + tipo de
 * cambio: el front pide los datos por HTTP como siempre.
 */

// ---------- Server -> Client ----------

export interface CustomerChangedDto {
  customerId: string;
  changeType: 'created' | 'updated' | 'deactivated' | 'archived';
}

export const CustomerSocketEvents = {
  /** Se aplicó un cambio de cliente; el front invalida ese cliente en su cache compartido. */
  Changed: 'customer.changed',
} as const;
