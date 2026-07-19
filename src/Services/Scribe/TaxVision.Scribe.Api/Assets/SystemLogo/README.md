# Logo de plataforma

Pegá acá el archivo `deploy.png` (ese nombre exacto). `ScribeSystemAssetSeeder` lo sube a
CloudStorage automáticamente al arrancar el servicio y guarda el `FileId` resultante en la tabla
`SystemAssetRefs` — no hace falta tocar `appsettings.json` ni ninguna variable de entorno.

Si el archivo no está presente al arrancar, el seeder lo loguea y sigue sin romper nada: los
correos se siguen enviando, simplemente sin logo en el header hasta que el archivo aparezca acá y
el servicio se reinicie.

Para reemplazar el logo más adelante: pegá el nuevo `deploy.png` (mismo nombre) encima del viejo y
reiniciá `scribe-api` — el seeder detecta que cambió (compara tamaño en bytes) y lo vuelve a subir
solo.
