# Reglas para este repositorio

## Qué es esto

**`BACKENDPERMISSIONS`** — copia de trabajo del backend de **TaxVision / StackVision**, un CRM SaaS
multi-tenant para oficinas de taxes y multiservices en EE. UU.

- 26 microservicios .NET 10 + Gateway YARP + **Communication** (Node / Fastify / Socket.IO).
- Se usa para implementar el **plan de autorización, RBAC y entitlements**.

Los otros dos repositorios del mismo plan:

| Repo | Qué es |
|---|---|
| `FRONTENDPERMISSIONS` | CRM Angular 21 — la aplicación del **staff** de la oficina |
| `CLIENTREDESIGN` | Portal Angular 19 — la aplicación de los **clientes** de la oficina, rediseñada |

**Este repo produce los contratos** (permissions, claims, entitlements, códigos de error) que los otros
dos consumen.

## Lee esto primero, en este orden

1. `docs/claude/PROMPT_NUBE.md` — qué hacer y cómo trabajar.
2. `docs/claude/PLAN.md` — el plan. Empieza por la **§Revisión 2026-09-26**, que dice qué sigue abierto.
3. `docs/claude/ARQUITECTURA_Y_REGLAS.md` — cómo está construido este repo y qué no se toca.
4. `docs/claude/CONTRATOS_ENTRE_REPOS.md` — la fuente de verdad compartida.
5. `docs/claude/TESTING.md` — comandos exactos de build, test y arranque.
6. `docs/claude/PROGRESO.md` — estado por fase. Se actualiza en cada fase.

`docs/claude/DECISIONES.md` es donde se anota toda decisión tomada sin intervención humana.

## Reglas de ramas y Pull Requests (INNEGOCIABLES)

1. Parte **siempre** desde `claude-trabajo`.
2. Trabaja en una rama propia: `claude/rbac-entitlements`.
3. **Nunca** abras un Pull Request hacia `main`. El PR va **siempre** hacia `claude-trabajo`.
4. **Nunca** hagas push, merge ni force push a `main` ni a `claude-trabajo`.
5. **Nunca** hagas merge de tus propios PRs. Los abres (draft al inicio) y los mantienes al día.
6. Un commit por fase, o varios pequeños dentro de la fase. Mensaje en el estilo
   `fase 3: validar permisos en gestión de roles`.
7. Antes de cada push, verifica la rama: `git branch --show-current`.
