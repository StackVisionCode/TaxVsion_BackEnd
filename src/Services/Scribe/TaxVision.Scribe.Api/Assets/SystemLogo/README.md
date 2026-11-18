# Logo de plataforma

`deploy.png` (ese nombre exacto) es el logo de respaldo de los correos: lo usan los correos de la
plataforma y los de las oficinas que no tienen un logo PNG, JPEG o GIF propio.

`ScribeSystemAssetSeeder` lo sube a CloudStorage al arrancar y guarda el `FileId` en la tabla
`SystemAssetRefs` solo en dos casos:

- No hay logo de respaldo guardado todavía (base nueva).
- El guardado ya no existe: CloudStorage o MinIO responden 404.

Si al arrancar no se puede comprobar (sin token, CloudStorage todavía arrancando, 5xx), el logo
guardado se respeta. La base manda: cambiar este archivo no cambia el logo de una base que ya tiene uno.

Para cambiar el logo en un entorno que ya tiene uno, usar
`PUT /scribe/system-assets/header-logo` (PlatformAdmin, PNG de hasta 1 MB).
