# Actualización local de Mobile y la app — 2026-11-19

Se reconstruyeron y reemplazaron los contenedores Auth, Mobile y Gateway desde este checkout. Se conservaron su configuración, puertos, red y volúmenes. No se recreó el resto de la flota.

- Auth y Mobile: `/health/live` y `/health/ready` responden 200 por la red interna.
- Gateway: ambos endpoints de salud responden 200 en localhost:5047.
- Las rutas de Mobile sin JWT responden 401.
- CORS desde localhost:8100: preflight 204 con origen y método permitidos.
- Se observaron GET autenticados 200 para vehículos, recorridos, estadísticas, serie diaria y configuración. El token antiguo se renovó correctamente tras la sincronización de permisos de Auth.
- Mobile está procesando eventos de permisos de Auth.

Las conexiones se verificaron contra DEV001. `TaxVision_Auth` tiene 152 migraciones aplicadas y `TaxVision_Mobile` tiene 2. El reporte posterior confirmó cero pendientes en ambas. No fue necesaria otra migración para adaptar la app.

La composición completa encontró variables locales ausentes de otros servicios. Se utilizó una composición parcial privada generada desde la configuración de los contenedores activos, sin alterar el resto de la flota. Las nuevas variables de Mobile se guardaron en la configuración local ignorada por Git. No se publican valores privados en este documento.

`D:/TaxPro-Milage` implementa el contrato nuevo, paginación, puntos por cursor, estados, revisiones, cola por cuenta, reintentos persistentes y estimaciones del servidor. Véase `D:/TaxPro-Milage/MOBILE_BACKEND_INTEGRATION.md` para cambios, comandos y límites.

Verificación cliente: compilación final correcta, 22 pruebas ChromeHeadless aprobadas y ESLint correcto sobre los servicios nuevos y adaptados. Ionic está activo en localhost:8100.

No se afirma una prueba física GPS ni una revisión visual: el host del navegador no estuvo disponible. La solicitud manual opcional de un token M2M fue rechazada por el control automático y no se ejecutó. Las consultas autenticadas y la mensajería real sí se observaron en los registros.
