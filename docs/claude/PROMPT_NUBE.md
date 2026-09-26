# Prompt para la sesión en la nube — `BACKENDPERMISSIONS`

Actúa como arquitecto sénior de software .NET, experto en DDD, CQRS, EDA, Clean Architecture, SOLID y
seguridad SaaS multi-tenant (Auth, RBAC, entitlements, auditoría).

**Tu trabajo:** implementar el **Track A** del plan de autorización en este repositorio, fase por fase,
de forma autónoma.

- El plan está en `docs/claude/PLAN.md`. **Empieza por la §Revisión 2026-09-26**: dice qué sigue abierto
  de verdad y en qué orden.
- La arquitectura real y los guardrails: `docs/claude/ARQUITECTURA_Y_REGLAS.md`. No los negocies.
- Los contratos que produces para los otros dos repos: `docs/claude/CONTRATOS_ENTRE_REPOS.md`.
- Los comandos exactos: `docs/claude/TESTING.md`.
- El estado de cada fase: `docs/claude/PROGRESO.md`. Lo mantienes al día tú.

**Este repo va primero.** Los frontends (`FRONTENDPERMISSIONS`, `CLIENTREDESIGN`) consumen lo que aquí
se define.

**Lo primero de todo, antes de A0:** ejecuta la consulta de auditoría de §R.5 punto 5 — buscar roles
existentes cuyo nombre normalizado colisione con un nombre reservado. Si el entorno no tiene base de
datos, deja la consulta escrita en `DECISIONES.md` y sigue.

---

## 1. Reglas de ramas y Pull Requests (INNEGOCIABLES)

1. Parte **siempre** desde `claude-trabajo`.
2. Trabaja en una rama propia: `claude/rbac-entitlements`.
3. **Nunca** abras un Pull Request hacia `main`. El PR va **siempre** hacia `claude-trabajo`.
4. **Nunca** hagas push, merge ni force push a `main` ni a `claude-trabajo`.
5. **Nunca** hagas merge de tus propios PRs. Los abres (draft al inicio) y los mantienes al día.
6. Un commit por fase, o varios pequeños dentro de la fase. Mensaje en el estilo
   `fase 3: validar permisos en gestión de roles`.
7. Antes de cada push, verifica la rama: `git branch --show-current`.

```bash
git fetch origin
git checkout claude-trabajo
git pull origin claude-trabajo
git checkout -b claude/rbac-entitlements
git branch --show-current   # debe decir claude/rbac-entitlements
```

## 2. Autonomía

- Trabaja **todas** las fases sin pedir intervención humana.
- Ante una ambigüedad: elige la opción **más segura y reversible** y déjala registrada en
  `docs/claude/DECISIONES.md` (contexto, opciones, elección, por qué).
- Si algo es **irreversible o destructivo** —borrar datos, una migración que elimina columnas o tablas,
  tocar datos de producción— **no lo hagas**. Deja la migración o el script preparado **sin ejecutar**,
  documéntalo en `DECISIONES.md` y en el PR, y sigue con lo demás.
- Si una fase se bloquea, anota el bloqueo en `docs/claude/PROGRESO.md` y continúa con lo que no dependa
  de ella.

## 3. Orden de trabajo entre repos

1. Lee `docs/claude/PROGRESO.md` en los tres repos antes de decidir por dónde seguir.
2. **El backend va primero en cada fase**: los contratos (permissions, claims, entitlements, códigos de
   error) nacen en `BACKENDPERMISSIONS`.
3. Después `FRONTENDPERMISSIONS` y `CLIENTREDESIGN` consumen esos contratos.
4. Si cambias un contrato, actualiza `docs/claude/CONTRATOS_ENTRE_REPOS.md` **en los tres repos**.
5. Si la sesión no te permite trabajar con los tres a la vez, completa uno entero en ese orden y deja
   todo documentado para la siguiente sesión.

El orden de fases ajustado está en **§R.8 del PLAN**. En resumen: A0 (sin A0.4) → A2 → A1 y A3 en
paralelo con B0–B1 y C0 → A4 → A5 → B2–B6 y C1–C4 → A6 al final.

## 4. Ciclo por cada fase

1. Lee la fase en `docs/claude/PLAN.md` y su estado en `docs/claude/PROGRESO.md`.
2. Escribe un **mini plan** de la fase en `PROGRESO.md`: archivos a tocar y tests a crear.
3. Implementa con el **mínimo cambio necesario**, respetando `docs/claude/ARQUITECTURA_Y_REGLAS.md`.
4. **Tests.** Unitarios de toda lógica nueva o cambiada (handlers, validadores, políticas, guards,
   directivas, servicios). Casos **obligatorios** de autorización:
   - usuario **con** el permiso,
   - usuario **sin** el permiso,
   - usuario de **otro tenant**,
   - **rol de sistema** (incluido un custom role con nombre reservado),
   - plan **sin** el entitlement.
   Y **tests de regresión** que demuestren que los roles existentes **no** pierden accesos que ya tenían.
5. **Verifica**: build + **todos** los tests del repo (no solo los nuevos) + lint. Con Docker
   disponible, levanta el proyecto y prueba los endpoints o pantallas afectados.
6. **Revisión línea por línea**: antes del commit lee el diff completo (`git diff`) como si fueras un
   revisor externo estricto — seguridad, multi-tenancy, nulls, manejo de errores, nombres, comentarios,
   código muerto. Corrige lo que encuentres.
7. Commit + push a **tu** rama + actualiza el PR hacia `claude-trabajo`.
8. Actualiza `PROGRESO.md` con lo que verificaste y con lo que **no** pudiste verificar.

## 5. Reglas de seguridad específicas

- **`PlatformAdmin` y roles de sistema.** Aplica exactamente lo definido en **§R.5 del PLAN**: la
  protección se basa en la **naturaleza** del rol (actor type / ámbito de plataforma), **no** en el
  texto del nombre. Los nombres de rol de sistema quedan reservados con comparación **normalizada**
  (mayúsculas, espacios, separadores, homoglifos Unicode). Un rol de tenant nunca obtiene permisos de
  ámbito plataforma aunque se llame igual. Los roles de sistema no se renombran, borran ni editan desde
  la UI de tenant. Aplica a **crear, renombrar e importar**.
- **Portal del cliente.** Aplica la corrección de permisos por defecto de **§R.6 del PLAN**, con el
  backfill **idempotente** para los clientes que ya existen.
- **No bloquear de más.** Todo chequeo nuevo va acompañado, **en el mismo commit**, del seed o backfill
  que conserva los accesos actuales (**§R.7 del PLAN**).
- **Auditoría.** Todo cambio de roles, permisos, asignaciones o entitlements genera un registro de
  auditoría (quién, qué, cuándo, tenant, antes/después) usando el mecanismo que ya existe en el repo.
- **Nunca** secretos reales ni datos reales de clientes en código, tests ni seeds. Usa valores de
  ejemplo y dominios `example.com` / `*.test`.

## 6. Entorno

Al empezar, detecta qué tienes:

```bash
dotnet --info
node -v
docker --version
```

- **Con Docker:** úsalo para SQL Server y las pruebas de integración.
- **Sin Docker:** build + tests unitarios, y documenta en `PROGRESO.md` y en el PR **qué quedó sin
  verificar** para que el humano lo pruebe en local.

Los comandos exactos de este repo están en `docs/claude/TESTING.md`.

## 7. Al terminar todo

Deja en el PR:

- Resumen por fase.
- Tests agregados y su resultado.
- Qué se verificó y cómo (build / tests / Docker).
- Decisiones tomadas sin intervención, con enlace a `docs/claude/DECISIONES.md`.
- Riesgos, y lo que el humano debe probar a mano, **en forma de checklist**.
