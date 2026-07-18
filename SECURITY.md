# Politica de Seguridad

TaxVision es un backend SaaS multitenant construido con microservicios en .NET 10.
Esta politica describe las versiones soportadas, como reportar vulnerabilidades y las
practicas de seguridad ya implementadas en el repositorio.

**Contacto de seguridad:** <stackvisionsoftware@gmail.com>
**Mantenedor:** Jorge Turbi

## Versiones soportadas

El proyecto se encuentra en desarrollo activo sobre la rama `Develop`. Solo la ultima
version de la rama principal recibe correcciones de seguridad.

| Componente | Version | Soportado |
| --- | --- | --- |
| Backend TaxVision (rama `main`) | ultima | :white_check_mark: |
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
- componente afectado (Gateway, Auth, Tenant, Customer, Subscription, Notification,
  CloudStorage o BuildingBlocks);
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

## Alcance

**Dentro de alcance:**

- servicios del backend en `src/` (Gateway, Auth, Tenant, Customer, Subscription,
  Notification, CloudStorage) y `BuildingBlocks`;
- autenticacion, autorizacion y aislamiento multitenant;
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

Los siguientes puntos son limitaciones conocidas para entornos productivos y **no**
deben considerarse vulnerabilidades reportables mientras esten documentados:

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
**Ultima actualizacion:** 2026-07-04
