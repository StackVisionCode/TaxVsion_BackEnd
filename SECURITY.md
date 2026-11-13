# Politica de Seguridad

TaxVision es una plataforma SaaS multitenant con servicios en .NET 10 y Node.js.
Esta politica describe las versiones soportadas, como reportar vulnerabilidades,
la superficie de ataque y las areas que requieren verificacion de seguridad.

**Contacto de seguridad:** <stackvisionsoftware@gmail.com>
**Mantenedor:** Jorge Turbi

## Versiones soportadas

El proyecto se encuentra en desarrollo activo sobre la rama `Develop`. Solo la ultima
version de la rama principal recibe correcciones de seguridad.

| Componente | Version | Soportado |
| --- | --- | --- |
| Servicios TaxVision (rama `main`) | ultima | :white_check_mark: |
| Ramas de desarrollo (`Develop`, feature branches) | actual | :white_check_mark: (esfuerzo razonable) |
| Versiones/tags anteriores | < ultima | :x: |
| .NET SDK / Runtime | 10.0.x | :white_check_mark: |

Las versiones de dependencias se fijan en `.csproj`, `global.json` y Dockerfiles.
Cuando una dependencia (ASP.NET, EF Core, Wolverine, RabbitMQ, Redis, YARP, etc.)
publique un parche de seguridad, se actualiza el pin correspondiente.

## Reportar una vulnerabilidad

Si encuentra una vulnerabilidad, **no abra un issue publico**. Reportela de forma
privada:

1. Envie un correo a **<stackvisionsoftware@gmail.com>** con el asunto
   `[SECURITY] TaxVision - <resumen>`, o
2. Use **GitHub Security Advisories** (pestana *Security* del repositorio ->
   *Report a vulnerability*).

Incluya en el reporte:

- descripcion del problema y su impacto (confidencialidad, integridad o disponibilidad);
- componente afectado (por ejemplo, Gateway, Auth, CloudStorage, Communication,
  Connectors o un worker);
- pasos de reproduccion o prueba de concepto;
- version, commit o rama afectada;
- cualquier `CorrelationId` relevante si observo el problema en ejecucion.

### Que esperar

| Etapa | Compromiso |
| --- | --- |
| Acuse de recibo | dentro de 72 horas habiles |
| Evaluacion inicial y severidad | dentro de 7 dias |
| Actualizaciones de estado | segun avance, al menos cada 7-14 dias |
| Resolucion o mitigacion | segun severidad; las criticas se priorizan |

Si el reporte es aceptado, coordinaremos una divulgacion responsable y le
acreditaremos si asi lo desea. Si es rechazado, explicaremos el motivo (por ejemplo,
comportamiento esperado o fuera de alcance).

Le pedimos no divulgar publicamente la vulnerabilidad hasta que exista una correccion
o mitigacion acordada.

## Contexto de seguridad y arquitectura

### Stack tecnologico

| Capa | Componentes relevantes |
| --- | --- |
| Cliente | Angular |
| APIs y servicios | .NET 10 con Entity Framework Core; Node.js/TypeScript con Fastify y Prisma en Communication |
| Datos y archivos | SQL Server para datos y metadatos; MinIO, compatible con S3, para objetos |
| Mensajeria y tiempo real | RabbitMQ, Socket.IO y Mediasoup (SFU WebRTC) |
| Ingesta y procesamiento | IMAP IDLE, ClamAV y transcripcion de audio mediante `whisper.cpp` |
| Infraestructura | Docker |

### Fronteras de confianza y puntos de entrada

1. **Cliente a servicios:** las peticiones HTTP/REST procedentes de Angular y
   los eventos de Socket.IO son entradas no confiables. Autenticar la conexion
   no sustituye la autorizacion de cada operacion sobre recursos concretos.
2. **Cliente a medios:** la senalizacion para Mediasoup y el trafico WebRTC
   atraviesan la frontera de Internet. La pertenencia a una sala y los permisos
   para crear transportes, productores y consumidores deben verificarse en el
   servidor, junto con los limites de recursos.
3. **Correo externo a servicios internos:** mensajes, cabeceras y adjuntos
   recibidos por IMAP IDLE proceden de remitentes no confiables, aunque la
   conexion al buzon este autenticada.
4. **Archivos a almacenamiento y procesamiento:** documentos, imagenes y audio
   pueden llegar a un area temporal de MinIO antes del escaneo. La frontera
   critica es su paso a un estado disponible, compartible o procesable tras la
   decision de ClamAV y las politicas de contenido. Los objetos rechazados o
   pendientes de revision requieren aislamiento y acceso restringido.
5. **Entre servicios:** mensajes de RabbitMQ y llamadas internas transportan
   datos entre procesos con privilegios distintos. Cada consumidor debe validar
   el contrato, la identidad o procedencia esperada y el `TenantId` antes de
   actuar. Las consultas de Entity Framework Core y Prisma acceden a SQL Server;
   el aislamiento multitenant debe mantenerse en cada lectura y escritura.
6. **Procesamiento de grabaciones:** el worker de transcripcion recibe trabajo
   y archivos derivados de sesiones de Communication. El acceso a grabaciones
   y transcripciones debe seguir los permisos del tenant y de los participantes.

### Prioridades de auditoria y pruebas

| Riesgo | Comprobacion prioritaria |
| --- | --- |
| Acceso indebido entre usuarios o tenants (IDOR/BOLA) | Probar identificadores ajenos en APIs, enlaces de MinIO, salas, grabaciones, transcripciones y consultas; verificar autorizacion del recurso en el servidor. |
| Inyeccion y datos manipulados | Revisar SQL dinamico o consultas sin parametros en EF Core/Prisma, contratos de RabbitMQ, eventos Socket.IO y parsing de cabeceras y adjuntos IMAP. |
| Subidas y antivirus | Comprobar que un objeto temporal no pueda descargarse, compartirse ni procesarse antes de una decision valida de escaneo; probar archivos infectados, fallos y tiempos de espera de ClamAV. |
| Tiempo real y denegacion de servicio | Probar autenticacion del handshake, revocacion y autorizacion por evento; limitar salas, transportes, productores, consumidores, ancho de banda y conexiones por usuario o tenant. |
| Workers y tareas asincronas | Probar reentregas, mensajes malformados, referencias cruzadas de tenant y fallos parciales sin publicar resultados no autorizados. |

Estas son **hipotesis y casos de prueba**, no vulnerabilidades confirmadas ni una
afirmacion de que todos los controles esten implementados. Un hallazgo debe
acompanarse de una ruta reproducible y del impacto observado.

## Alcance

**Dentro de alcance:**

- servicios y workers en `src/` (incluidos Gateway, Auth, Tenant, Customer,
  Subscription, Notification, CloudStorage, Connectors y Communication) y
  `BuildingBlocks`;
- autenticacion, autorizacion y aislamiento multitenant;
- API HTTP, Socket.IO, senalizacion y medios WebRTC, colas RabbitMQ e ingesta IMAP;
- subida, escaneo, almacenamiento, acceso y transcripcion de archivos;
- manejo de tokens (JWT, refresh tokens, tokens de invitacion, bootstrap);
- exposicion de secretos, credenciales o datos entre tenants;
- configuracion de despliegue en `deploy/`.

**Fuera de alcance:**

- vulnerabilidades en dependencias de terceros ya publicadas (repórtelas al proyecto
  upstream; aun asi agradecemos el aviso para actualizar el pin);
- ataques que requieran acceso fisico o credenciales de administrador legitimas;
- configuraciones de ejemplo con secretos de tipo `replace-with-...`;
- entornos de desarrollo local sin TLS (ver "Consideraciones de despliegue").

## Modelo de seguridad implementado

TaxVision ya incorpora los siguientes controles (ver README, seccion 14):

### Identidad y aislamiento multitenant

- cada usuario esta aislado por `TenantId`; el mismo email puede existir en tenants
  distintos con indice unico `(TenantId, Email)`;
- el Gateway elimina cualquier `X-Tenant-Id` enviado por el cliente y lo reconstruye
  desde el claim firmado `tenant_id` del JWT;
- el control plane (`PlatformAdmin`) vive en un tenant interno reservado
  (`Kind = Platform`), no comercial, que no puede suspenderse ni recibir suscripciones.

### Autenticacion y credenciales

- **sin registro publico**: el alta de usuarios es exclusivamente por invitacion, con
  una matriz fija de quien puede invitar a quien;
- **passwords** con PBKDF2 (salt aleatorio de 16 bytes, HMAC-SHA256, 100.000
  iteraciones, salida de 32 bytes) y comparacion en tiempo constante; minimo 12
  caracteres;
- **tokens de invitacion y refresh tokens**: se genera un valor aleatorio, se devuelve
  en claro **una sola vez** y solo se persiste su hash SHA-256;
- **JWT** firmado con HMAC-SHA256, compartido entre Auth, Tenant y Gateway; los roles
  se derivan del `ActorType` y nunca se aceptan nombres de rol arbitrarios del cliente;
- **refresh token rotatorio** con revocacion idempotente; un tenant inactivo no puede
  renovar ni iniciar sesion.

### Bootstrap del PlatformAdmin

- el primer `PlatformAdmin` se crea mediante un bootstrap secreto y temporal
  (`PlatformBootstrap:*` en User Secrets); solo se guarda el SHA-256 del token y nunca
  se escribe en logs. Debe deshabilitarse y eliminarse tras aceptar la invitacion.

### Superficie HTTP y abuso

- solo el Gateway publica API al host; los servicios internos no se exponen
  directamente;
- rate limiting por IP y path en endpoints sensibles (login, refresh, crear/aceptar
  invitacion, crear tenant): 10 req/min sin cola;
- `ExceptionHandlingMiddleware` devuelve `ProblemDetails` con codigo y `CorrelationId`
  sin exponer stack traces;
- errores SQL de unicidad (2601/2627) se convierten en conflicto controlado, evitando
  condiciones de carrera.

### Secretos

- la configuracion local vive en `.env` (ignorado por Git) y User Secrets; no se
  guardan copias con secretos reales en el repositorio;
- no se cachean credenciales, tokens ni operaciones de escritura en Redis.

## Consideraciones de despliegue (pendientes conocidos)

Los siguientes puntos requieren revision antes de un despliegue productivo.
Si alguno permite una explotacion concreta, reportelo mediante el canal privado:

- reemplazar credenciales locales por un secret manager en produccion;
- mover el bootstrap de `PlatformAdmin` a un secret manager o Job de provisioning;
- habilitar TLS externo e interno segun el entorno;
- agregar CI/CD, SBOM y escaneo de secretos.

## Divulgacion responsable

Agradecemos la investigacion de seguridad realizada de buena fe. No emprenderemos
acciones legales contra quien reporte de forma responsable, respete la privacidad de
los datos, evite la degradacion del servicio y no acceda ni modifique datos ajenos mas
alla de lo necesario para demostrar el problema.

---

**Licencia del codigo:** propietaria; consulte [LICENSE](LICENSE).
**Ultima actualizacion:** 2026-11-13
