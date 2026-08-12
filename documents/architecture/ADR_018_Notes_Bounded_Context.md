# ADR-018 — Notes como Bounded Context propio

**Estado**: Aceptado — **plan de 11 fases CERRADO** (2026-08-06). Fase 0-3 cimientos (scaffolding,
dominio, persistencia, RBAC); Fase 4/4B rate limiting + proyección de Customer; Fase 5/6 Application
+ API; Fase 7 adjuntos CloudStorage (Caso B); Fase 9 endurecimiento de autorización; Fase 10
hardening final (este documento). Ver `Implementaciones/Notes/03_Plan_De_Fases.md` para el detalle
de cada fase.
**Fecha**: 2026-08-06
**Contexto de la decisión**: el senior planteó desacoplar el antiguo "Planner" (Notes, Task,
Calendar, Reminder) en bounded contexts separados. Tras el análisis (ver `Implementaciones/Notes/00_Overview_Decisiones_Y_Alcance.md` §8) se concluyó que partir en 5 servicios reintroduce el
monolito distribuido; Reminder nunca es un servicio (mecanismo de scheduling compartido); Task y
Calendar son los dos subdominios grandes defendibles; **Notes es el más defendible** de los cinco
como microservicio propio.

> **Nota (2026-08-12) — el veredicto sobre Reminder respondía a otra pregunta.** «Reminder nunca es
> un servicio» sigue siendo cierto para lo que se estaba evaluando: un *mecanismo de scheduling
> compartido* reutilizable por Task y Calendar es una biblioteca, no un bounded context. La pregunta
> distinta — «¿existe un subdominio de negocio *recordatorio de usuario*, con máquina de estados e
> invariantes propios?» — se respondió que sí, y Reminder se implementó como microservicio
> independiente. Ver **`ADR_021_Reminder_Bounded_Context.md` §1**. Este ADR **no se revoca**: el
> `IReminderScheduler` genérico que descartó sigue descartado, y es un non-goal explícito de ADR-021.

---

## 1. Decisión

Notes es un **microservicio independiente**, con **base de datos propia**, puerto dev **5440**,
host interno `http://notes-api:8080`. Se adopta porque:

1. **No tiene ningún invariante de consistencia inmediata que cruce a otro contexto** — una nota
   sobre un Customer/Task/Appointment no necesita ser transaccionalmente consistente con el padre;
   la referencia es una asociación blanda, resuelta en display-time.
2. **Ningún otro servicio la llama en su write path** — actualizar un Customer no toca Notes. Esto
   la mantiene fuera del *Entity Services Antipattern* (Nygard, ver `00_...` §1).
3. Es **genuinamente transversal** (se adjunta a Customer, Task, Appointment, TaxCase, etc.), como
   la modelan los CRMs grandes (Salesforce `ContentNote` + `ContentDocumentLink` polimórfico,
   HubSpot notes como *engagement*).

**Guardrail permanente del servicio:** las notas se escriben directo contra la API de Notes desde
el frontend/staff; **ningún otro microservicio debe llamar a Notes en su write path.** Reintroducir
esa llamada es señal de acoplamiento — rechazarlo.

## 2. Bounded context — qué hace y qué NO

**Hace:** CRUD de notas del staff, asociables polimórficamente vía `NoteReference { TargetType,
TargetId }` (1 target por nota, YAGNI respecto al M:N de Salesforce); contenido HTML sanitizado;
visibilidad `Private | Team | ClientVisible`; adjuntos referenciados a CloudStorage (Notes no
guarda bytes); color/label + pin (orden fijo `IsPinned DESC, UpdatedAt DESC`, sin orden manual);
eventos `notes.note_created/updated/deleted.v1` + `notes.attachment_detached.v1` para un futuro
MyPlanner (read model/BFF) — Notes no conoce a MyPlanner.

**No hace:** no maneja usuarios/roles/permisos (consume proyecciones de Auth); no guarda bytes
(CloudStorage es el único dueño); no es dueño de Customer/Task/Appointment (los referencia por ID
opaco); no maneja reminders/scheduling; no permite que el CustomerPortal cree o edite notas (solo
lee `ClientVisible`); no implementa mensajería cliente→staff (eso es Communication).

## 3. Decisiones de arquitectura (resumen de las 9 ADR internas del plan)

- **Visibilidad como VO del dominio** (`Private | Team | ClientVisible`), no un flag booleano —
  `ClientVisible` es la única visible al CustomerPortal, que **lee, nunca edita**.
- **"Admin ve todas" es un permiso (`notes.view_all`), no un hardcode de rol** — ver ejecución en
  `NoteVisibilityPolicy` (Fase 5) y el endurecimiento de Fase 9. Ver todas ⇒ sí; editar la ajena ⇒
  no (solo el autor edita contenido, sin excepción incluso con `notes.view_all`).
- **Adjuntos vía CloudStorage, Caso B** (subida directa del navegador con el JWT del usuario) — cero
  MinIO/M2M de Notes hacia CloudStorage en v1; Notes solo consume `FileAvailable/Infected/
  BlockedByPolicy/Deleted` (Fase 7) para mover el adjunto Pending→Available/Rejected/Detached. El
  M2M saliente que sí existe (Fase 4B, único de v1) es de **lectura** hacia Customer, para la
  proyección delgada `CustomerDirectoryEntry` — nunca hacia CloudStorage.
- **Proyección de Customer obligatoria** (no diferida, a diferencia del plan original) —
  `CustomerDirectoryEntry` valida SOFT al crear (nunca bloquea por lag de proyección) y resuelve
  nombres para mostrar/buscar sin N+1. Alimentada por 6 eventos granulares + reconciliación batched
  para la importación masiva (que no trae `DisplayName` por diseño de Customer).
- **RBAC/RateLimit/Multi-tenant/Session-denylist son obligatorios desde Fase 0** — mismas 4 guías
  globales que todo microservicio nuevo del monorepo (`README.md` §45.5).

## 4. Consecuencia arquitectónica

- Referencias a otros contextos **por ID opaco**; comunicación entre contextos **solo por eventos**
  (Wolverine + RabbitMQ, exchange `taxvision-events`); cero llamadas síncronas de consistencia
  hacia/desde Notes salvo el único M2M de lectura documentado arriba.
- El endurecimiento de autorización de Fase 9 (`IsOwnerOrHasManageHandler<Note>`, sin permiso de
  override) se aplicó solo a los endpoints de edición de contenido — Archive/Restore/Delete
  conservan el chequeo OR (autor **o** `notes.view_all`) en la capa de Application
  (`NoteVisibilityPolicy.CanManage`), porque el mecanismo genérico de ownership no puede expresar
  dos reglas de override distintas para el mismo tipo de recurso sin romper la regla
  "ve-no-edita" — decisión documentada en el propio `NotesController.cs`.

## 5. Non-goals explícitos (para cortar scope-creep)

Reminders/scheduling; orden manual de notas; notas colaborativas en tiempo real; versionado de
contenido más allá de `UpdatedAt`; búsqueda full-text (v1 usa SQL simple, categoría H); mensajería
cliente→staff (es Communication); M2M/MinIO de Notes hacia CloudStorage.

## 6. Referencias

- `Implementaciones/Notes/00_Overview_Decisiones_Y_Alcance.md` — documento maestro con las 9 ADR
  internas completas (ADR-01 a ADR-09).
- `Implementaciones/Notes/01_Modelo_De_Dominio.md` — aggregate `Note`, VOs, invariantes.
- `Implementaciones/Notes/02_Contratos_Integracion_Y_Proyecciones.md` — eventos pub/sub + CloudStorage.
- `Implementaciones/Notes/03_Plan_De_Fases.md` — el plan ejecutable de 11 fases.
- `Implementaciones/Notes/04_Guardrails_Checklist_Y_Verificacion.md` — 36 guardrails mapeados.
- `README.md` §47 — endpoints, permisos, políticas de rate limit y configuración de Notes.
