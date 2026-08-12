# ADR-021 — Reminder como Bounded Context propio

**Estado**: Aceptado — **plan de 10 fases CERRADO** (2026-08-12). Fase 0-2 cimientos (scaffolding,
dominio, persistencia); Fase 3 RBAC; Fase 4 rate limiting + M2M; Fase 5 Quartz (`AdoJobStore`); Fase
6 Application + API; Fase 7 contratos de integración; Fase 8 entrega vía Notification + Scribe; Fase
9 observabilidad + fitness functions; Fase 10 hardening, docs y E2E real (este documento). Ver
`Implementaciones/Reminder/03_Plan_De_Fases.md` para el detalle de cada fase.
**Fecha**: 2026-08-12
**Contexto de la decisión**: el análisis del antiguo "Planner" (Notes, Task, Calendar, Reminder)
concluyó que **Reminder nunca es un servicio**, y ese veredicto quedó escrito en ADR-018. Al abordar
el trabajo real se vio que respondía a **otra pregunta** — ver §1.

---

## 1. Por qué esto contradice en apariencia a ADR-018

ADR-018 dice, textualmente, que «Reminder nunca es un servicio (mecanismo de scheduling
compartido)». Ese veredicto es correcto **para la pregunta que respondía**: si se parte el Planner en
cinco servicios, cada uno con su propio scheduler, *ese* Reminder — un mecanismo genérico de
scheduling reutilizable por Task, Calendar y quien venga — no es un bounded context, es una
biblioteca.

La pregunta de ADR-021 es distinta: **¿existe un subdominio de negocio "recordatorio de usuario" con
invariantes propios?** Sí, y son suyos, no de quien lo agenda:

- Un recordatorio puede sobrevivir a su objetivo (`Missed` cuando la ventana de gracia expira) —
  Calendar no tiene ese estado.
- Anclado vs absoluto (ADR-R-03) es una decisión **del usuario al crearlo**, no del objetivo.
- Snooze con tope es un ciclo de vida propio, con su propia máquina de estados.
- La idempotencia por `RequestKey` (ADR-R-07) es un invariante del recordatorio, no del evento.

Un servicio con máquina de estados, invariantes y ciclo de vida propios es un bounded context. Un
`IReminderScheduler` compartido no lo sería. **ADR-018 no se revoca**: se anota que su veredicto
respondía a la pregunta del mecanismo, no a la del subdominio.

## 2. Decisión

Reminder es un **microservicio independiente**, con **base de datos propia**, puerto dev **5500**,
host interno `http://reminder-api:8080`. Los disparos los agenda **Quartz.NET** con `AdoJobStore`
sobre esa misma base — un trigger por recordatorio (ADR-R-04), un solo scheduler para todos los
tenants (ADR-R-05).

**Guardrail permanente del servicio:** Reminder define **sus propios tres contratos de entrada**
(`reminder.requested/target_moved/target_closed.v1`) y **no importa** los eventos de dominio de
Calendar, Task ni de ningún otro contexto. Si mañana Signature o Correspondence quieren
recordatorios, hablan esos mismos tres y Reminder no cambia una línea. Importar
`CalendarIntegrationEvents` desde Reminder es la señal de acoplamiento — la fitness function
`No_Reminder_type_should_reference_a_neighbouring_bounded_context` está puesta para rechazarlo.

## 3. Bounded context — qué hace y qué NO

**Hace:** ciclo de vida completo del recordatorio de un usuario del tenant — crear (idempotente),
reprogramar, mover el ancla, posponer con tope, descartar, cancelar, marcar perdido; agendado real
con Quartz + reconciliación + retención; publicación de `reminder.due.v1` con el contenido completo
del aviso.

**No hace:** **no entrega avisos** (ADR-R-02) — ni SMTP, ni SignalR, ni tokens de push; no maneja
usuarios/roles/permisos (consume proyecciones de Auth); no es dueño de Calendar/Task/Note (los
referencia por ID opaco + `Category`); no tiene recordatorios compartidos entre usuarios (v1); no
tiene recordatorios del CustomerPortal (invariante R1: siempre de un usuario del tenant); no infiere
la zona horaria del usuario (ADR-R-06 — viene en el request, Auth todavía no la publica).

## 4. Decisiones de arquitectura (las 8 ADR internas del plan)

| # | Decisión | Por qué |
|---|---|---|
| **ADR-R-01** | Reminder define sus propios 3 contratos de entrada; no importa los de Calendar/Task | Con contratos ajenos, cada contexto nuevo que quiera recordatorios sería un consumer nuevo **dentro** de Reminder |
| **ADR-R-02** | Reminder es dueño del **CUÁNDO**; Notification entrega | Notification ya tiene preferencias, categorías, fan-out y FCM. Duplicarlo dejaría medio Notification adentro en seis meses |
| **ADR-R-03** | Anclado vs absoluto **explícito** en el aggregate (`IsAnchored`) | Sin la distinción aparece «moví la tarea y el aviso se fue con ella cuando yo no quería», o su inverso |
| **ADR-R-04** | **Un trigger de Quartz por recordatorio**, no un barrido | Es para lo que Quartz existe. Umbral de revisión en §6 |
| **ADR-R-05** | **Un solo scheduler** para todos los tenants; `TenantId` en el `JobDataMap`, *trigger group* = `tenant:{id}` | Un scheduler por tenant no escala: el `ISchedulerFactory` liga la connection string por scheduler |
| **ADR-R-06** | La **zona horaria viene en el request**, validada con `IanaTimeZone.TryNormalize` | Evita una proyección de usuarios solo para leer un `TimeZoneId` que Auth hoy no publica |
| **ADR-R-07** | **Idempotencia obligatoria** por `RequestKey` con índice único | La entrega es *at-least-once*. Sin esto un redelivery duplica el aviso. Es el fallo más probable de todo el diseño |
| **ADR-R-08** | La **ventana del reschedule** se acepta y se documenta | Ver §5. Consecuencia asumida, no bug |

RBAC / RateLimit / multi-tenant fail-closed / session-denylist son obligatorios desde la Fase 0 —
mismas guías globales que todo microservicio nuevo del monorepo (`README.md` §45.5).

## 5. Consecuencia aceptada — la ventana del reschedule (ADR-R-08)

```
14:30  el usuario mueve la cita 15:00 → 17:00
14:30  Calendar publica reminder.target_moved.v1 (desde su outbox)
14:45  si el consumer está atascado, Quartz dispara el aviso viejo
       → «tu cita de las 15:00 empieza en 15 min», y ya no existe a esa hora
```

Con el recordatorio dentro de Calendar (misma transacción) esto era **imposible**. Separado, es
posible y poco probable. **No es teórico**: este monorepo ya tuvo 10.412 envelopes atascados en
Correspondence (2026-08-07).

**Mitigación**: vigilar el lag de consumers. No hay solución estructural sin volver a acoplar. Queda
escrito aquí como consecuencia asumida para que dentro de un año nadie lo reporte como bug.

## 6. Umbral de revisión de Quartz (ADR-R-04)

Los locks cluster-wide de Quartz degradan pasados ~3 nodos, y `QRTZ_TRIGGERS` con centenares de miles
de filas empieza a pesar en el polling.

**Umbral escrito**: si `QRTZ_TRIGGERS` supera **50.000 filas activas** o el despliegue pasa de **3
réplicas** de Reminder, reevaluar hacia un modelo de horizonte (Quartz agenda solo la ventana de las
próximas N horas, alimentado por un barrido). **Hasta ese punto, un trigger por recordatorio es lo
correcto y lo simple.**

Operación día a día (misfire, clustering, NTP, serialización, reconciliación, retención): `README.md`
§49.5.

## 7. Non-goals explícitos (para cortar scope-creep)

Entrega de notificaciones (es Notification — ADR-R-02); recordatorios compartidos entre varios
usuarios; recordatorios recurrentes con RRULE; recordatorios del CustomerPortal; inferir la zona
horaria del usuario; un `IReminderScheduler` genérico reutilizable por otros servicios (eso es
exactamente lo que ADR-018 descartó, y sigue descartado).

## 8. Referencias

- `Implementaciones/Reminder/00_Overview_Decisiones_Y_Alcance.md` — documento maestro con las 8 ADR
  internas completas (ADR-R-01 a ADR-R-08).
- `Implementaciones/Reminder/01_Modelo_De_Dominio.md` — aggregate `Reminder`, VOs, invariantes R1-R7.
- `Implementaciones/Reminder/02_Contratos_Integracion_Y_Proyecciones.md` — los 3 eventos de entrada +
  `reminder.due.v1`.
- `Implementaciones/Reminder/03_Plan_De_Fases.md` — el plan ejecutable de 10 fases.
- `Implementaciones/Reminder/04_Guardrails_Checklist_Y_Verificacion.md` — guardrails y checkpoints.
- `README.md` §49 — endpoints, permisos, rate limit, operación de Quartz, observabilidad y entrega.
- `ADR_018_Notes_Bounded_Context.md` §1 — el veredicto «Reminder nunca es un servicio» y por qué
  respondía a otra pregunta.
