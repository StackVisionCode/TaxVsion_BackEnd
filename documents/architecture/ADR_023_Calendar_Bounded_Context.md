# ADR-023 — Calendar como Bounded Context propio

**Estado**: Aceptado — **plan de 11 fases CERRADO** (2026-08-14). Fases 0-2 cimientos (scaffolding,
dominio del tiempo, recurrencia y excepciones); 3 persistencia; 4 RBAC; 5 rate limiting + M2M; 5B
directorio de clientes; 6 tipos, disponibilidad y conflictos; 7 Application + API + Reminder;
8 Communication; 9 Notification + Scribe; 10 export `.ics` + observabilidad + fitness functions;
11 hardening y docs (este documento). Ver `Implementaciones/Calendar/03_Plan_De_Fases.md`.
**Fecha**: 2026-08-14
**Contexto**: el análisis del antiguo "Planner" agrupaba Notes, Task, Calendar y Reminder en un solo
servicio. Los otros tres ya salieron con contexto propio (ADR-018, ADR-021, ADR-022); Calendar es el
cuarto y último, y el que concentra el problema que ninguno de los otros tenía: el tiempo.

---

## 1. Por qué Calendar no es un módulo del Planner

El resto del Planner trata el tiempo como un dato: una tarea vence, una nota se creó. Calendar lo
trata como un modelo, y uno que se contradice consigo mismo dos veces al año.

«Todos los lunes a las 9:00» no es un instante: es una hora de pared más una zona más una regla. Si
se guarda en UTC —13:00Z porque Nueva York está en UTC−4 en verano— al entrar el invierno la reunión
aparece a las 8:00 y nadie sabe por qué. Ese es el invariante de este servicio, y no se comparte con
nadie: mezclarlo con el motor de dependencias de Task o con el ciclo de vida de una nota obligaría a
que tres modelos de tiempo distintos convivan bajo el mismo límite de consistencia.

---

## 2. Las 13 decisiones

| # | Decisión | Por qué |
|---|---|---|
| ADR-C-01 | Calendar dueño del **compromiso**; Communication dueño de la **sala**. Enlace por eventos, nunca HTTP síncrono | `Meeting.ScheduledForUtc` ya existía: sin esta decisión hay dos agendas |
| ADR-C-02 | **Sin sync externo en v1**, con el costo real escrito | Los scopes actuales son sólo de correo: sincronizar exige re-consentimiento de todos. Ver §5 |
| ADR-C-03 | Recurrente = **hora local + tz + RRULE**, jamás UTC. `UNTIL` sí en UTC (RFC 5545) | El bug de DST se manifiesta dos veces al año y en silencio. Ver §3.1 |
| ADR-C-04 | Ocurrencias **calculadas al vuelo**, no materializadas. Filas sólo para excepciones | Opuesto a Task, y correcto por la razón opuesta. Ver §3.2 |
| ADR-C-05 | `EditScope` **obligatorio y explícito**; `ThisAndFollowing` **parte la serie** | Sin esto, editar el futuro reescribe el pasado |
| ADR-C-06 | **All-day es una fecha, no un instante** | Guardarlo como medianoche UTC lo corre un día para media humanidad |
| ADR-C-07 | Solapamiento = **advertencia por defecto**, bloqueo sólo si el tipo lo exige | Un preparador puede querer solapar a propósito. Bloquear siempre es paternalista; no avisar nunca es inútil |
| ADR-C-08 | Los asistentes son **snapshot**, no FK a Customer/Auth | La cita del año pasado debe mostrar el nombre que tenía entonces. Ver §3.3 |
| ADR-C-09 | Sólo el **organizador** mueve o cancela; los asistentes sólo responden | Sin esta regla, dos personas mueven la misma cita a la vez |
| ADR-C-10 | `Ical.Net` para RRULE — **el mismo que Task** | Un solo motor de recurrencia en el monorepo |
| ADR-C-11 | **Export `.ics` de solo lectura** | 5% del costo de sincronizar y cubre el 80% del caso. Ver §4 |
| ADR-C-12 | `TargetId` de Reminder **compuesto y determinista** por ocurrencia | Reminder identifica su objetivo con un solo id, y una serie tiene N ocurrencias |
| ADR-C-13 | **Un solo motor de conversión** hora local ↔ UTC, con política explícita para la hora inexistente y la ambigua | Medido: los dos motores del servicio difieren una hora entera en la ambigua. Ver §3.1 |

---

## 3. Las tres que se ganaron midiendo

### 3.1 `WallClock` es el único motor de conversión (ADR-C-13)

El servicio tiene dos motores a mano: `Ical.Net`/NodaTime y `TimeZoneInfo`. **Coinciden en las horas
normales y difieren una hora entera en la hora ambigua** —la que ocurre dos veces al terminar el
horario de verano—, y uno de los dos **lanza** en la hora que no existe. Sin excepción y sin log.

`WallClock` concentra las dos políticas: la hora que no existe se **rechaza al crear** y **corre hacia
adelante al expandir**; la ambigua resuelve a la primera de las dos ocurrencias.

De paso cayó una trampa que el modelo no preveía: **`EST` es un id IANA válido** y resuelve a «SA
Pacific Standard Time» —Bogotá, UTC−5 **sin horario de verano**—, así que quien lo escribe pensando en
Nueva York recibe las citas corridas medio año. `MST` y `HST` igual. `CalendarTimeZone` exige la forma
canónica `Area/Location`.

### 3.2 Cero ocurrencias materializadas (ADR-C-04)

Una serie de tres años son 156 ocurrencias y **una** fila. Materializarlas no sólo multiplica el
almacenamiento: deja datos que quedan mal el día que un país cambia sus reglas de DST, porque el
instante guardado ya no corresponde a la hora local que el usuario eligió.

El costo es que la consulta de rango carga **todas** las series del tenant y las expande en memoria.
Medido con la flota arriba: **69,5 ms por consulta con 43 series**, produciendo 516 ocurrencias. El
umbral de revisión escrito es **2.000 series activas por tenant**; a partir de ahí hay que cachear las
expansiones en Redis por `(tenant, rango)` con invalidación por evento. No antes: sería complejidad
sin problema medido. `occurrence_expansion_duration_ms` y `series_count_per_tenant` existen para ver
llegar ese momento.

### 3.3 El snapshot del asistente resolvió un problema ajeno

`AttendeeSnapshot` guarda nombre y correo del día de la cita. Se decidió por la razón histórica —la
cita del año pasado debe mostrar el nombre que tenía entonces— y terminó resolviendo lo que había
bloqueado la entrega de correos en Reminder: **Notification no tiene directorio de `userId` → email**.
Como los correos viajan **dentro del evento**, el consumer no tiene a quién preguntarle.

---

## 4. El feed `.ics` (ADR-C-11)

```
GET /calendar/feed/{userId}/{token}.ics     ← sin sesión: el token de la URL es la credencial
```

Google y Outlook pollean el archivo sin poder autenticarse: no hay dónde meter un JWT. La credencial
es la URL, con el mismo trato que un enlace público de Drive:

- **32 bytes de CSPRNG, SHA-256 en base, valor crudo una sola vez.** El plan pedía HMAC firmado; se
  descartó porque «revocable» obliga a una fila igual, y con la fila la firma sólo agrega una segunda
  forma de validar credenciales en el repositorio. Se copió `ShareToken` de CloudStorage.
- **`404` para todo**: token inválido, revocado y de otro usuario responden igual. Distinguir convierte
  la URL en un buscador de qué usuarios existen.
- **Límite por token, no por IP** — Google pollea desde direcciones rotativas.
- **Ventana −30/+365 días** y `Cache-Control: private, max-age=900`.
- **Con la base caída se sirve la última copia buena** en vez de un 500: ante un error Google deja de
  actualizar, ante un archivo viejo muestra lo de ayer. Revocar borra esa copia, o el botón de revocar
  no serviría durante una caída.

La serie sale como **una** `VEVENT` con su `RRULE`; las excepciones como `EXDATE` (cancelada) y como
`VEVENT` con `RECURRENCE-ID` (movida). Serializado con `Ical.Net`, nunca armando el texto a mano: una
`EXDATE` mal escrita no rompe el import, lo deja a medias.

---

## 5. El sync bidireccional que no se hizo (ADR-C-02)

Conectar Google Calendar y Microsoft Graph en los dos sentidos exige scopes nuevos
(`calendar.events`), y los actuales de Connectors/Postmaster son **sólo de correo**: activarlo obliga
a **re-consentimiento de todos los usuarios ya conectados**. A eso se suma el cursor de sincronización
por cuenta, el eco (un cambio propio vuelve como cambio ajeno) y la resolución de conflictos cuando
los dos lados se movieron.

El diseño está escrito en `Implementaciones/Calendar/05_V2_Sync_Externo_Google_Microsoft.md`, en
cuatro niveles, y **no está aprobado**. El nivel 1 —leer disponibilidad sin almacenar nada— resuelve
la doble reserva, que es el 80% del dolor, sin cursor, sin eco y sin conflictos.

---

## 6. El reparto con Communication

| | Calendar | Communication |
|---|---|---|
| Dueño de | el compromiso: cuándo, quién asiste, si choca | la sala: enlace, código corto, participantes conectados |
| Guarda | `MeetingId` de la sala | nada de la cita |
| Ante caída del otro | la cita se crea igual; la sala aparece sola al volver | — |

Communication **no lleva columna `AppointmentId`**: Calendar ya guarda el `MeetingId`, así que el
evento de reagendado lo lleva, y el vínculo vive en un solo lado.

El job de reparación pide la sala con `calendar.appointment_meeting_room_requested.v1` y **no**
republicando `appointment_scheduled`: desde que ese evento lleva destinatarios, republicarlo
reenviaría la invitación a todo el mundo.

---

## 7. Consecuencias

**A favor.** El modelo del tiempo queda encerrado donde se puede probar: 138 tests, de los cuales los
de DST van contra SQL Server real porque InMemory no reproduce que `datetime2` vuelva con
`DateTimeKind.Unspecified`. Tres fitness functions propias impiden las regresiones que costarían más
caro: que una ocurrencia se convierta en entidad, que una serie gane un `StartUtc`, y que un segundo
tipo conozca los contratos de Communication.

**En contra.** Una consulta de rango más cara que un `SELECT`, y un servicio más que desplegar. El
umbral de revisión de §3.2 es la respuesta a lo primero; lo segundo era el precio de los cuatro
contextos del Planner y ya se pagó tres veces.

**Lo que queda abierto.** El sync externo (§5), y la retención de series **sin fin**: una serie sin
`UNTIL` ni `COUNT` no tiene última ocurrencia, así que `CalendarRetentionJob` **no la borra nunca** —
purgarla por su fecha de creación borraría la reunión semanal que el despacho tiene desde hace ocho
años y sigue teniendo.
