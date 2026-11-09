/**
 * Orden de los avisos de estado (activar/desactivar). Con prefetch y reintentos llegan desordenados:
 * con fecha, la fila solo cambia si el aviso es más nuevo que el último aplicado, y la guarda. Sin
 * fecha (retiro, reconciliación) se aplica como siempre.
 */
export function newerStatus(statusChangedAtUtc?: Date) {
  return statusChangedAtUtc
    ? { OR: [{ StatusChangedAtUtc: null }, { StatusChangedAtUtc: { lt: statusChangedAtUtc } }] }
    : {};
}

export function stamp(statusChangedAtUtc?: Date) {
  return statusChangedAtUtc ? { StatusChangedAtUtc: statusChangedAtUtc } : {};
}
