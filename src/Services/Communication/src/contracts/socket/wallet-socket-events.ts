/**
 * Wallet realtime. El servicio Wallet (.NET) avisa cuando cambia el saldo de un tenant (recarga, reserva,
 * consumo); acá se relaya a la sala STAFF del tenant (`t:{tenantId}:staff`) para que el pill de saldo y el
 * apartado Wallet del front se refresquen sin acción del usuario. Payload vacío — el front re-consulta por HTTP.
 */

// ---------- Server -> Client ----------

export const WalletSocketEvents = {
  /** Cambió el saldo del monedero; el front refresca saldo + historial. */
  Updated: 'wallet.updated',
} as const;
