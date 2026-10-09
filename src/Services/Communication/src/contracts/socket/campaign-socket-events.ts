/**
 * Campaigns realtime. El servicio Campaigns (.NET) publica el avance de un envío por el bus; acá se
 * relaya a la sala STAFF del tenant (`t:{tenantId}:staff`) para que el módulo Campaigns del front
 * actualice el estado del run sin refrescar a mano. Payload mínimo — solo ids: el front pide los datos
 * por HTTP como siempre. No persiste nada: es puro relay realtime.
 */

// ---------- Server -> Client ----------

export interface CampaignRunUpdatedDto {
  campaignId: string;
  runId: string;
}

export const CampaignSocketEvents = {
  /** Cambió el estado de un run (arrancó / resultado de un envío / cerró); el front recarga los runs. */
  RunUpdated: 'campaign.run.updated',
} as const;
