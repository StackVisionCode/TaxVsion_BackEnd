# ADR-022 — Task como Bounded Context propio

**Estado**: Aceptado — **plan de 11 fases CERRADO** (2026-08-13); **Fase 12 (`ClientRequest`)
implementada** (2026-08-14) tras decidirse que el portal del cliente entra en el producto. Fases 0-2 cimientos (scaffolding,
dominio, persistencia); 3 RBAC; 4 rate limiting + M2M; 4B directorio de clientes; 5 motor de
dependencias; 6 subtareas; 7 Application + API; 8 recurrencia + Reminder; 8B petición al cliente;
9 plantillas fiscales; 10 adjuntos + observabilidad + fitness functions; 11 hardening y docs; 12 portal del cliente (este
documento). Ver `Implementaciones/Task/03_Plan_De_Fases.md`.
**Fecha**: 2026-08-13
**Contexto**: el análisis del antiguo "Planner" agrupaba Notes, Task, Calendar y Reminder en un solo
servicio. Notes y Reminder ya salieron con contexto propio (ADR-018, ADR-021); Task es el tercero, y
el que concentra las reglas de negocio de verdad.

---

## 1. Por qué Task no es un módulo del Planner

Un Planner monolítico obligaría a que el motor de dependencias, la recurrencia, las plantillas
fiscales y el ciclo de vida de una nota compartieran el mismo límite de consistencia y el mismo
despliegue. No comparten ni invariantes ni ritmo de cambio: una nota se crea y se edita, una tarea
participa en un grafo con contadores que hay que reconciliar.

El criterio no fue "cuántas tablas tiene" sino **de quién es el invariante**. `OpenBlockerCount` sólo
lo puede mover el motor de dependencias; ninguna otra parte del Planner tiene por qué poder tocarlo.

---

## 2. Las 14 decisiones

| # | Decisión | Por qué |
|---|---|---|
| ADR-T-01 | Bounded context propio, DB propia, puerto 5510 | El invariante del grafo no se comparte |
| ADR-T-02 | `DependencyType` con **un solo valor** (`FinishToStart`) | Ver §3.1 |
| ADR-T-03 | `Blocked` es condición derivada, no estado almacenado | Ver §3.2 |
| ADR-T-04 | `TaskSeries` aggregate propio, **una instancia abierta a la vez** | Ver §3.3 |
| ADR-T-05 | La subtarea **es** una tarea con `ParentTaskId` | Agregados chicos, referencia por identidad |
| ADR-T-06 | `TaskDependency` es agregado propio | Es una relación *entre* agregados: no vive en ninguno |
| ADR-T-07 | Contadores en la fila + job de reconciliación | Un `COUNT` por fila no sobrevive a un listado de 200 |
| ADR-T-08 | Ciclos detectados en un *domain service*, bajo `UPDLOCK` | Un recorrido de grafo no cabe en un invariante de agregado |
| ADR-T-09 | Task **no entrega avisos**: pide a Reminder | Misma separación que ADR-R-02 |
| ADR-T-10 | El timer lo arranca la persona, nunca el sistema | Cierra el bug 5B.5 del legacy |
| ADR-T-11 | Task guarda `fileId`, **nunca bytes** | CloudStorage es el único dueño del contenido |
| ADR-T-12 | Desadjuntar **no borra** el archivo | Otros servicios referencian el mismo objeto |
| ADR-T-13 | La instancia #2 de una serie **no hereda** los adjuntos de la #1 | El 941 del Q2 no lleva los papeles del Q1 |
| ADR-T-14 | Lo que ve el cliente es `ClientRequest`, **nunca** la tarea | Ver §7 |

---

## 3. Las cuatro discrepancias con el diseño previo del Planner — con evidencia

### 3.1 Los cuatro tipos de dependencia → sólo Finish-to-Start

El análisis del legacy dice, textual: *«dependencias FS/SS/FF/SF modeladas pero nunca usadas»*. El
diseño previo (§19.1) las arrastraba igual.

Complejidad especulativa con **track record medido de cero uso**: SS/FF/SF vienen de planificación de
obra, y en una firma fiscal la única relación real es «no puedo empezar B hasta terminar A». El enum
existe con un solo valor y la columna está en la tabla: sumar los otros tres el día que haya un caso
real es una migración trivial, tenerlos sin usar es superficie que hay que testear y explicar para
siempre.

### 3.2 `Blocked` como estado almacenado → condición derivada

El diseño previo mezclaba dos conceptos en un campo, y eso produce un bug concreto: una tarea
`InProgress` recibe una dependencia nueva y pasa a `Blocked`; al resolverse, **¿a qué estado vuelve?**
Hay que guardar el estado anterior, y ahí empieza el pantano.

Separados: el **progreso** lo mueve el usuario (`Status`), el **bloqueo** lo lleva el motor
(`OpenBlockerCount`, con `IsBlocked` calculado), y la **espera de negocio** es un estado propio
(`WaitingOnClient`). Nadie "vuelve" a ningún lado porque el estado nunca se perdió. El invariante es
una guarda, no una transición.

**Verificado**: `grep` de `PreviousStatus` vacío en todo el servicio; fitness function que prohíbe
comparar el estado contra texto.

### 3.3 Expansión de recurrencia por adelantado → serie + una instancia abierta

El diseño previo generaba instancias futuras con un job diario y decía explícitamente *«heredado del
legacy»*. Generar por adelantado produce lo que ningún preparador quiere en marzo: 40 tareas abiertas
de «941 trimestral».

Además incorporaba una distinción que el diseño previo no tenía y que en la práctica decide el
resultado: `FixedSchedule` (el Q2 vence el 15 de junio aunque te atrasaras con el Q1) frente a
`AfterCompletion` (90 días desde que efectivamente lo hiciste).

**Medido durante la implementación**: `CalDateTime` de Ical.Net interpreta el `DateTime` como hora
**local de la zona**, así que una semilla en UTC corría la serie entera por el offset. Es la clase de
error que sólo aparece con una zona con horario de verano.

### 3.4 «`TaskWatcher` (child aggregate)» → entidad hija

*«Child aggregate»* no existe en DDD: o es entidad hija dentro del límite de consistencia, o es
agregado propio referenciado por id. Watchers quedó **fuera de v1** y declarado en los non-goals:
mejor ausente que fantasma —que es exactamente el bug 5B.6 del legacy—.

---

## 4. Los seis bugs del legacy y su cierre

| # | Bug | Cómo se comprobó que murió |
|---|---|---|
| 5B.1 | Dos modelos de Task irreconciliables | Una sola tabla; Kanban y Calendar leen la misma fila con distinta proyección |
| 5B.2 | Catálogos GUID hardcodeados | El motor lee `TaskItemStatus`; las etiquetas del tenant son taxonomía aparte |
| 5B.3 | «¿Completada?» por string en 4 variantes y 2 idiomas | Fitness function con regex sobre las 4 capas, **vista fallar** con una violación deliberada |
| 5B.4 | Recurrencia en JSON opaco + tabla paralela | Una columna RRULE; cero `JSON.parse` y cero `try/catch` de parsing en el servicio |
| 5B.5 | Auto-start del timer al crear | Fitness function: `StartTimer` sólo se referencia desde su propio handler, **vista fallar** |
| 5B.6 | Subtareas y watchers fantasma | Subtareas con endpoints reales y probados; watchers ausentes y declarados en los non-goals |

---

## 5. Lo que se midió y contradijo al plan

Documentado en detalle en `Implementaciones/Task/03_Plan_De_Fases.md`, cierre de cada fase. Lo que
cambió una decisión:

- **EF Core devuelve `datetime2` como `Unspecified`**, y el dominio exige UTC: la serie dejaba de
  materializar la ocurrencia siguiente **en silencio**. Corregido con convertidores en el DbContext,
  no con un parche en el materializador.
- **Una instancia de owned type no se puede compartir** entre entidades: un `TaskReference` común a
  seis tareas dejó cinco sin cliente en la base, y ningún test unitario lo veía porque comparaban
  valor y no identidad.
- **El veredicto del escaneo se publica una sola vez.** Un adjunto creado después se quedaba en
  `Pending` para siempre. Obligó a lo que el Overview había descartado: un cliente M2M hacia
  CloudStorage — de solo metadatos, `GET /storage/internal/files/{id}/scan-status`, sin tocar bytes.
- **Dos de las tres plantillas fiscales no son grafos**: la 1040-ES y la 941 son el mismo encargo
  cuatro veces al año, así que la plantilla ganó recurrencia opcional y aplicarla abre una serie.
- **`TaskStatus` choca con `System.Threading.Tasks.TaskStatus`** (CS0104) y el error sale fuera de
  Domain: el enum se llama `TaskItemStatus`.

---

## 6. El portal del cliente (ADR-T-14)

El atajo evidente el día que piden «que el cliente vea sus pendientes» es añadir
`[AllowActorTypes(ActorType.CustomerPortal)]` a los controllers de Task. Eso le enseñaría las notas
internas, el asignado y los códigos de facturación del mismo objeto, y corrompería las dos métricas a
la vez: capacidad del staff y responsividad del cliente dejarían de medir lo que dicen.

`ClientRequest` es un agregado propio con su ciclo (`Pending → Submitted → Accepted|Rejected|Cancelled`),
sus endpoints de portal y su cuota de rate limit aparte —un cliente subiendo su W-2 no puede quedarse
sin turno porque la firma esté trabajando—. **El cliente nunca cierra el pedido**: sube y queda
`Submitted`; aceptarlo es del preparador, mismo criterio que T-C3.

El guardrail no es una revisión de código: una fitness function exige que sólo los controllers del
namespace `Portal` declaren `CustomerPortal`, y se vio fallar con una violación deliberada.

**Lo que se midió al implementarlo:**

- El precedente que se iba a copiar —`PortalNotesController`— aceptaba el `targetId` por query y sólo
  filtraba por tenant: una cuenta de portal podía leer las notas visibles de otro cliente del mismo
  tenant. Se cerró derivando el cliente del token, como ya hacía CloudStorage.
- **El directorio de clientes de Notification estaba vacío** (0 filas). Tenía consumers de eventos
  pero ningún job de reconciliación, así que los clientes anteriores al consumer nunca entraron. El
  efecto no era de esta fase: **ningún correo dirigido a un cliente salía de Notification**, incluido
  `task.waiting_on_client.v1`. Con el job nuevo pasó a 8297 direcciones y los correos empezaron a
  salir.
- Sembrar permisos por migración EF **no actualiza las proyecciones locales** de los servicios: sólo
  el evento lo hace. Un permiso recién sembrado da 403 hasta que `SystemRolePermissionsSyncService`
  lo republica.
- Una pasada de arranque que publica eventos necesita esperar a `WaitForApplicationStartedAsync`, o
  Wolverine todavía no está listo y la primera corrida se pierde entera.

---

## 7. Lo que queda fuera y por qué

- **Watchers**, **facturación automática desde timers**, **comentarios en la tarea** (eso es Notes).
- **Dependencia «hasta que haya adjunto»**: mezcla dos motores. El flujo correcto ya existe — la
  tarea «recibir documentos» se completa a mano y eso desbloquea la siguiente.
- **Recordatorios al cliente**: `Tasks:ClientReminders:Enabled` existe en `false` y no hay job que lo
  consuma. El flag está para que encenderlos sea una decisión explícita —y coordinada con Signature y
  Correspondence, que le escriben al mismo cliente—, no un descubrimiento.
