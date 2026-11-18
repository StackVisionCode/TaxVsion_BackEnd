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

/** Mismo control para los avisos de perfil (nombre, email, foto): con fecha, solo gana el más nuevo. */
export function newerProfile(profileChangedAtUtc?: Date) {
  return profileChangedAtUtc
    ? { OR: [{ ProfileChangedAtUtc: null }, { ProfileChangedAtUtc: { lt: profileChangedAtUtc } }] }
    : {};
}
