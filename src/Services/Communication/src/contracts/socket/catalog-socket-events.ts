/**
 * CAT-F5.2 — Catalog realtime. Subscription (.NET) avisa cuando el admin edita el catalogo publico
 * (plan creado / version publicada / precio / addon creado / lifecycle). Comunicacion relaya un
 * broadcast tenant-agnostico a TODOS los sockets conectados para que landing, CRM y portal refetcheen
 * `GET /subscription/plans` y `GET /subscription/addons` sin accion del usuario. Payload minimo — el
 * frontend re-consulta por HTTP para traer el estado nuevo.
 */

// ---------- Server -> Client ----------

export const CatalogSocketEvents = {
  /** Algo del catalogo publico cambio; el frontend refetchea planes y addons. */
  Changed: 'catalog.changed',
} as const;

/** Payload del evento `catalog.changed`. Informativo — todos los consumers refetchean el listado. */
export interface CatalogChangedDto {
  /** Qué cambió: `plan.created`, `plan.metadata`, `plan.version_published`, `plan.prices`,
   * `plan.modules`, `plan.deprecated`, `plan.archived`, `addon.created`, `addon.prices`. */
  changeKind: string;
  /** Id del plan o add-on que cambió. */
  entityId: string | null;
  /** Code del plan o add-on. */
  entityCode: string | null;
}
