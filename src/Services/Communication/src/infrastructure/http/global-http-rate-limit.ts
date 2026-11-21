/** Rutas que CloudStorage llama con el token de la persona para la biblioteca de archivos. */
export const FILE_LIBRARY_ROUTE_PREFIX = '/communication/files/';

export function isFileLibraryRoute(url: string): boolean {
  return url.startsWith(FILE_LIBRARY_ROUTE_PREFIX);
}

/**
 * ¿Cuenta esta petición en el límite global por IP? Todo lo que llega por el Gateway trae `CF-Connecting-IP`. La
 * llamada interna de CloudStorage a `/communication/files/*` no lo trae: con la IP del contenedor, todas las
 * búsquedas de la plataforma compartirían un solo cupo. Esas rutas ya tienen su cupo por usuario.
 */
export function globalHttpRateLimitApplies(url: string, cfConnectingIp: string | undefined): boolean {
  return !(isFileLibraryRoute(url) && !cfConnectingIp);
}
