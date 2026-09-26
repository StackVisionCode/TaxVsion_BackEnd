# Arquitectura real de este repo y guardrails

Todo lo de aquí está leído del código, no de la documentación. Si algo de un README contradice esto,
**manda el código**.

---

## 1. Cómo está construido

### Capas (Clean Architecture + DDD)

Cada microservicio vive en `src/Services/<Servicio>/` con cuatro proyectos. Hay dos convenciones de
nombre conviviendo y **ninguna se unifica ahora**:

- `Auth/{Api,Application,Domain,Infrastructure}`
- `Customer/TaxVision.Customer.{Api,Application,Domain,Infrastructure}`

| Capa | Qué vive ahí | Qué **no** |
|---|---|---|
| `Domain` | Aggregates, value objects, domain events, invariantes | **Cero LINQ**, cero EF Core, cero `virtual`, cero setters públicos |
| `Application` | Commands, queries, handlers, consumers, puertos (interfaces de repositorio) | Nada de EF ni HTTP |
| `Infrastructure` | EF Core, repositorios, clientes HTTP, jobs, persistencia | — |
| `Api` | Controllers, atributos de autorización, rate limit, `Program.cs` | Lógica de negocio |

Lo compartido está en `src/BuildingBlocks/`: `ActorTypeAuthorization`, `Authorization`,
`BuildingBlocks.Web`, `Messaging`, `Persistence`, `Security`, `Sessions`, `Tenancy`, `RateLimiting`,
`CustomerVisibility`, `Results`, `Common`, `Domain`, `Caching`, `Permissions`, `TimeZones`.

**Communication** es aparte: Node + Fastify + Socket.IO + Prisma, en
`src/Services/Communication/`. No comparte BuildingBlocks; tiene su propio verificador de JWT, sus
proyecciones y sus consumers.

### CQRS y mensajería (Wolverine + RabbitMQ)

- Los handlers son **estáticos**, con dependencias por parámetro. Wolverine los descubre por
  ensamblado.
- Exchange fanout `taxvision-events`, con inbox/outbox durables.
- **Publicar un evento nuevo exige `PublishMessage<T>().ToRabbitExchange(...)` en el `Program.cs` del
  servicio que lo publica.** Sin esa línea, `bus.PublishAsync` lo descarta **en silencio**: sin
  excepción y sin log de error.
- **Consumir un evento nuevo exige que la cola del servicio esté bindeada al exchange**
  (`ListenToRabbitQueue(...).BindExchange(...)`). Wolverine descubre el handler igual, pero nunca lo
  ejecuta.
- **Primera línea de todo consumer:** `using var _ = correlation.Push(evt.CorrelationId);`. Sin
  excepción: es lo que hace posible el tracing entre servicios.
- La transacción de Wolverine abre **antes** del handler.

### Multi-tenancy

- El tenant sale del claim `tenant_id` y lo puebla `JwtTenantContextMiddleware`, que va **antes** de
  `UseAuthorization()` — en modo `Projection`, `[HasPermission]` necesita el tenant ya poblado durante
  su propia evaluación.
- Filtro global de tenant en EF Core, **fail-closed**: sin tenant en contexto se compara contra
  `Guid.Empty` (cero filas). **Nunca** se omite el filtro por defecto.
- Un handler de Wolverine invocado **sin request HTTP** (consumer, `bus.InvokeAsync` local) corre en
  otro scope de DI: el filtro global tira aunque el `tenantId` recibido por parámetro sea válido. En
  esos repositorios se usa `IgnoreQueryFilters()` **y se confía en el parámetro explícito**, nunca en
  el ambiente.
- **Ningún query ni command nuevo puede cruzar tenants.** Si recibe un `tenantId` por el body o la
  ruta, hay que comparar contra el del token salvo que sea un endpoint `PlatformOnly` explícito.

### Autorización — las cuatro capas, en orden

1. **Superficie** — `[AllowSurface]` + `SurfaceAuthorizationFilter`, fail-closed. Un token con claim
   `surface` (el Account del Landing) solo entra donde se declara. Es un filtro **de MVC**: no llega a
   Communication, que rechaza cualquier `surface` por su cuenta (`Auth.SurfaceNotAllowed`).
2. **Actor type** — `[AllowActorTypes(...)]` + `ActorTypeAuthorizationFilter`, **global y
   fail-closed**: una acción sin el atributo se bloquea con 403. Hay un fitness test que falla el build
   si falta.
3. **Permission** — `[HasPermission(code)]` → `PermissionPolicyProvider` → `IUserPermissionsSource`.
   En modo `Projection` lee la proyección local del servicio y compara `perm_v`; si el token está
   atrasado responde `401 Auth.TokenStale`. Aquí corre también el **module gate** (hoy log-only).
   - `[HasPermissionForActor(actorType, code)]` exige un permiso **solo** a un actor type, para
     endpoints compartidos entre staff y portal. Apilar dos `[HasPermission]` **no** sirve: ASP.NET
     exige todas las policies y dejaría fuera al staff.
4. **Ownership / scope** — `IsOwnerOrHasManageHandler`, visibilidad por asignación
   (`customers.view_all` como bypass) y scope por `customer_id` en el portal.

**Reglas que no se negocian:**

- `[Authorize(Roles = ...)]` **no** es lo mismo que `[AllowActorTypes]`. No se mezclan.
- **Nunca** leer el claim `perm` directo: ya no se emite. Todo pasa por `IUserPermissionsSource`.
- Un endpoint M2M o de admin que toma el tenant del **body** necesita `PlatformOnly` **explícito**.
- Los permisos de un cliente M2M (`ActorType.Service`) son estáticos por registro en
  `ServiceAuth:Clients`: **no** usan `perm_v` ni proyección, leen el claim directo.
- La denylist de sesión por `sid` (Redis) tiene que estar cableada en todo servicio nuevo, o un usuario
  deslogueado sigue autenticado ahí.

### Seguridad

- **La barrera real es siempre el backend.** El frontend solo oculta o adapta la UI.
- Secretos de webhook: comparación en **tiempo constante**, y rechazo explícito si el secreto esperado
  está vacío o por defecto.
- Nada de secretos en código. Local va por `dotnet user-secrets`; producción por
  `deploy/docker/docker-compose.yml`.
- Un permiso nuevo para un cliente M2M se agrega en **tres** sitios: user-secrets (local),
  `docker-compose.yml` (producción) y, si el endpoint es nuevo, el catálogo de Auth. Arreglarlo solo en
  local y dejar producción rota es el fallo clásico.
- Las claves de configuración M2M deben calzar **exacto** con el `SectionName` de las Options; un typo
  produce un fallo silencioso de auth, no una excepción clara.
- Un token M2M se cachea en memoria del proceso: reiniciar Auth **no alcanza**, hay que reiniciar
  también el consumidor.

### Auditoría

Todo cambio de roles, permisos, asignaciones o entitlements deja un registro con **quién, qué, cuándo,
tenant y antes/después**, usando el mecanismo que ya existe (`IAuthAuditWriter` en Auth,
`CustomerAuditLogs` en Customer, `StorageAccessLog` en CloudStorage). No se inventa un mecanismo nuevo.

---

## 2. Guardrails de implementación

### Diseño de dominio

1. **SRP en handlers.** Un handler que pase de ~30 líneas de lógica se parte en métodos privados con
   nombres que dicen **qué** hacen, no cómo: una fase por método (load → validate/mutate →
   persist+publish).
2. **Nada de métodos genéricos en aggregates.** Cero `ChangeStatus(newStatus)` con cinco reglas dentro.
   Cada mutación es su propio método explícito, con su validación y su domain event. Cero `switch` de
   estado.
3. **Value objects, no primitivos sueltos.** Todo dato con reglas propias (tamaño, formato, secreto
   cifrado) es un VO que valida en construcción.
4. **Cero LINQ en aggregates.** Vive en repositorios (Infrastructure) o queries (Application).
5. **Sin `virtual` ni setters públicos** en aggregates. Constructor privado + factory `Create()`,
   backing fields, sin lazy loading.
6. **Un archivo, una clase.** Los value objects van en `Domain/ValueObjects/`.
7. Los repositorios devuelven `Result<T>` cuando el fallo es una regla de negocio; `null` solo cuando
   "no existe" es semánticamente distinto de "error".

### EF Core

- Propiedad computada tipo `AsReadOnly()` sobre un campo privado: necesita `builder.Ignore()` explícito
  o rompe las migraciones.
- PK `Guid` en entidades de una colección de navegación: `ValueGeneratedNever()`, o EF genera `UPDATE`
  en vez de `INSERT`.
- **Un owned type no se comparte entre filas hermanas.** Pasar la misma instancia a varias filas hace
  que EF la rastree una sola vez y las demás se inserten con NULL. Cada fila copia el suyo.
- Un `DbContext` sin DbSets igual necesita migración inicial, o el bootstrap de Wolverine falla en el
  primer arranque.
- `InMemory` no aplica `unique` ni `rowversion`: esos casos se prueban con SQL Server real.

### Migraciones

- Una migración se crea **y se aplica en el acto**. Una migración sin aplicar produce drift y una
  cascada de 503 en el Gateway.
- **Nunca** se conceden permisos de sistema por INSERT de migración: el sync no ve diferencia, no
  publica el evento y las proyecciones quedan viejas. La vía es `PermissionCatalog` (código) +
  migración `HasData` **solo** para la fila del permiso + sync al arrancar Auth.
- Toda migración de este plan debe ser **aditiva** (columnas nullable). Nada que borre columnas o
  tablas: se deja preparada, sin ejecutar, y se documenta.

### Comentarios y nombres

- **Comentarios en español, simples y directos.** Una línea cuando baste. Explican el **porqué** o el
  invariante no obvio, no repiten el código.
- Nada de narrar el historial del bug ("Bug real de producción (fecha): se encontró durante…"). Eso va
  en el mensaje del commit. Nada de fechas, nada de referencias cruzadas a fases o a otros servicios.
- Si en un archivo que **ya estás tocando** encuentras comentarios largos o redundantes, simplifícalos.
  **No** hagas limpieza masiva en archivos que no tocas.
- **Identificadores de código en inglés.** El namespace debe decir el ensamblado.
- Los archivos de test se nombran **por la clase que prueban**, sin números de fase.

### Alcance

- **Mínimo cambio necesario por fase.** Nada de refactors fuera del alcance de la fase.
- No se introducen patrones nuevos ni librerías nuevas sin justificarlo en `DECISIONES.md`.
- Se respetan los patrones existentes aunque haya otros mejores: la coherencia vale más aquí.
