# Progreso — Track A (`BACKENDPERMISSIONS`)

Inicializado el **2026-09-26** con el estado real encontrado en la auditoría (§R.2 y §R.3 del PLAN).
**La sesión en la nube mantiene esta tabla al día: una fila por fase, actualizada al cerrarla.**

Estados: **NI** no iniciada · **EN CURSO** · **P** parcial · **C** completa · **BLOQUEADA** · **OB** obsoleta.

| Fase | Estado | Rama | Commits | Tests agregados | Verificación | Pendientes |
|---|---|---|---|---|---|---|
| A0 — Hotfixes de seguridad | **P** | — | — | — | — | A0.4 (refund) ya estaba **C**. Abiertas: A0.1 bypass de rol en **3 sitios**, A0.2 nombres reservados, A0.5 DMCA, A0.6 IDOR de llamadas, A0.7 broadcasts, A0.8 private links, A0.9 M2M |
| A1 — Ownership de recurso | **NI** | — | — | — | — | Signature (14 sub-recursos), Tasks, Correspondence, Customer, CloudStorage, Campaigns |
| A2 — Deny layer y propagación | **P** | — | — | — | — | G2 ya estaba **C** (`UserAccessResolver.cs:31-32`). Abiertas: fan-out por titular, consumidores de `RolePermissionsChanged`, jerarquía en deactivate, `Reason`/`ExpiresAtUtc` |
| A3 — Baseline y catálogo | **NI** | — | — | — | — | Employee (75 permissions) sigue sin Campaigns ni Notes |
| A4 — Techo y API de roles | **NI** | — | — | — | — | `Grantable(...)` unificado, `GET /auth/roles/{id}/users`, reactivar rol |
| A5 — Bootstrap, errores, realtime | **NI** | — | — | — | — | Sin `/auth/me/access`, sin `access.changed`, sin `IAuthorizationMiddlewareResultHandler`. **Debe nacer consciente de la superficie** (§R.4.1) |
| A6 — Entitlement enforcement | **NI** | — | — | — | — | `Authorization:ModuleGate:Enforce` no existe en ninguna configuración. Va **al final** y solo con B7 y C5 desplegados |
| A7 — Tests y observabilidad | **P** | — | — | — | — | Ya hay fitness de catálogo y de superficie. Falta la matriz actor × permission × deny × plan × **asignación** |
| A8 — Documentación | **NI** | — | — | — | — | — |

## Qué verificar antes de dar una fase por cerrada

1. `dotnet build TaxVision.slnx -c Release` sin errores.
2. El gate **exacto** del CI en verde (ver `TESTING.md`), redirigido a un archivo — **nunca** a `tail`.
3. `npm run typecheck && npm test` en `src/Services/Communication` si la fase lo tocó.
4. `dotnet csharpier check` **de los archivos de la fase**.
5. La migración de la fase **aplicada**, no solo creada.
6. El test de regresión que demuestra que ningún rol existente perdió accesos (§R.7 del PLAN).

## Bloqueos

*(ninguno registrado todavía)*

## Notas del estado inicial

- El repo está **al día** con el trabajo cerrado el 2026-09-25/26: plan Account/Manage Subscription
  (F1–F14), ABAC por asignación (P1+P2), retiro de empleados (F0–F3) y el permiso `portal.folders.view`
  ya aplicado.
- Existe una **cuarta superficie** que el plan original no cubría: el Account del Landing
  (`[AllowSurface]`). Toda fase que toque endpoints debe tenerla en cuenta.
- Hay fitness tests de superficie que fallan si un endpoint nuevo no declara lo que debe: si uno se
  pone rojo, **la respuesta es actualizar la lista, no borrar el test**.
