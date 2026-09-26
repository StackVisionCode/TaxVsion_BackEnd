# Decisiones tomadas sin intervención humana

Una entrada por decisión, con este formato:

```
## <fecha> — <título corto>
**Contexto:** qué situación la obligó.
**Opciones:** las que se consideraron.
**Elección:** la que se tomó.
**Por qué:** la razón, en una o dos frases. La opción más segura y reversible gana.
**Reversible:** cómo se deshace.
```

---

## 2026-09-26 — Track C va en `CLIENTREDESIGN` (D-A12)

**Contexto:** el plan dejaba abierto en qué repo del Portal se implementa el Track C.
**Opciones:** `CLIENTTAXPROFRONTEND` (el original) o `CLIENTREDESIGN` (el rediseño en curso).
**Elección:** `CLIENTREDESIGN`.
**Por qué:** es la copia que se despliega y la que se prepara para esta implementación; hacerlo en el
original obligaría a portar el trabajo dos veces.
**Reversible:** sí; el Track C no toca el backend.

## 2026-09-26 — Se retiraron del repo tres artefactos de depuración con tokens reales

**Contexto:** `gitleaks` encontró JWT reales commiteados en `scratch_portal.json`, `scratch_state.json`
y `svc_token.txt`, en la raíz del repo.
**Opciones:** dejarlos y solo reportarlos · borrarlos del árbol y añadirlos a `.gitignore` · reescribir
el historial.
**Elección:** borrarlos del árbol, añadir `scratch_*.json`, `svc_token.txt` y `*.log` al `.gitignore`, y
reportar los tokens para que el humano los rote. **No** se reescribió el historial.
**Por qué:** son artefactos de depuración sin valor para el proyecto y contienen credenciales en un repo
remoto. Reescribir el historial es destructivo y estaba prohibido.
**Reversible:** sí, `git revert` del commit los devuelve.

## 2026-09-26 — Un correo personal real en fixtures se reemplazó por uno ficticio

**Contexto:** un correo de Gmail real, capturado de una bandeja de entrada durante una depuración,
aparecía 11 veces en tres archivos de test de Connectors y Correspondence.
**Opciones:** dejarlo · reemplazarlo por un dominio de ejemplo.
**Elección:** reemplazarlo por `manuel.mena@example.com`.
**Por qué:** es dato personal real en un repositorio remoto, y el valor concreto no aporta nada a los
tests.
**Reversible:** sí, pero no hay motivo para revertirlo.

## 2026-09-26 — Los enlaces de Firebase del README se reportan, no se cambian

**Contexto:** el `README.md` enlaza dos PDF de Firebase Storage con su token de descarga en la URL.
**Opciones:** quitar los enlaces · reemplazar el token por un placeholder · dejarlos y reportarlos.
**Elección:** dejarlos y reportarlos para que el humano rote esos tokens.
**Por qué:** son documentación del propio equipo; romper los enlaces sin preguntar es peor que el riesgo
de un token de lectura de dos PDF. La decisión de rotarlos es del humano.
**Reversible:** n/a.
