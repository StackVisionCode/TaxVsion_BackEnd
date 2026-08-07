# ADR-019 — La superficie interna no se expone en el Gateway

**Estado**: Aceptado — implementado el 2026-08-07 (Fase 2 del plan de remediación de 49 hallazgos,
`Implementaciones/Documentacion Completa/90_Plan_Remediacion_Hallazgos.md` §6, hallazgos GW-01 y
GW-02).
**Fecha**: 2026-08-07

---

## 1. Contexto

El sistema tiene **18 controllers M2M** repartidos en 8 servicios: endpoints que solo debe llamar
otro microservicio (aprovisionar un tenant durante el onboarding, resolver un token de invitación,
leer los rate limits de un plan, reconciliar la proyección de clientes…). Todos exigen
`[Authorize(Policy = "ServiceOnly")]` + `[AllowActorTypes(ActorType.Service)]`.

El Gateway (YARP) **no transforma paths**: reenvía el path tal cual llega. Su tabla tiene 25 rutas,
todas de la forma `/{prefijo-de-servicio}/{**catch-all}` — no hay catch-all global.

De ahí salía una asimetría que nadie había declarado: la accesibilidad de un controller interno desde
internet dependía **solo de cómo estaba escrito su `[Route]`**.

- 7 controllers usaban `internal/*` a secas ⇒ ninguna ruta del Gateway los alcanzaba.
- 11 controllers llevaban el prefijo del servicio delante (`auth/internal/...`,
  `customers/internal/...`, `tenants/internal/...`, `subscriptions/internal/...`,
  `payments-app/internal/...`) ⇒ caían dentro del catch-all de su cluster y **estaban en la
  superficie pública de internet**.

La autorización aguantaba —sin un token de servicio válido no se pasa—, pero eso es una sola capa,
y OWASP **API9:2023 Improper Inventory Management** existe justamente porque una superficie que no
debería ser alcanzable y lo es acaba siéndolo el día que alguien se equivoca en un atributo.

## 2. Decisión

**La superficie M2M no es alcanzable desde el Gateway, y eso se garantiza por dos mecanismos
independientes.** Que se pueda quitar uno sin reabrir el agujero es el punto de la decisión, no un
detalle de implementación.

### 2.1 Defensa por comportamiento — `InternalSurfaceGuardMiddleware`

Un middleware en el Gateway, **antes de CORS y de la autenticación**, devuelve **404 sin cuerpo** a
cualquier petición cuyo path contenga un segmento `internal`.

- **404 y no 403.** OWASP admite explícitamente el 404 para no confirmar la existencia del recurso.
  Un 403 le regalaría a quien sondea el mapa de la superficie interna.
- **Antes de auth.** No tiene sentido gastar validación de token en una petición que se va a
  rechazar, y el 404 debe salir igual con credenciales o sin ellas.
- **Comparación por segmento**, no `Contains("/internal/")`: así atrapa los paths que *terminan* en
  el segmento y deja pasar un recurso legítimo que solo empiece por esas letras
  (`/documents/internal-audit`).
- **`LogWarning` + `gateway_internal_surface_probes_blocked_total`.** Cualquier valor > 0 de ese
  contador es un evento de seguridad: el M2M legítimo no pasa por acá.

### 2.2 Defensa por estructura — convención `internal/*` unificada

Los 18 controllers usan ya `internal/*` sin prefijo de servicio. Como el Gateway solo enruta por
prefijo de servicio, **no existe ruta que los alcance**. Si mañana alguien borra el middleware, el
Gateway sigue sin tener por dónde entrar.

Dos fitness functions congelan la convención (`InternalRouteConventionFitnessTests`):

1. Ningún `[Route]` del repo puede tener un segmento `internal` que no sea el primero.
2. Ninguna ruta del `appsettings.json` del Gateway puede mencionar el segmento `internal`.

### 2.3 Orden de ejecución: primero el guard, después el renombrado

El guard se puso **antes** de renombrar. Con el guard en su sitio, un path mal actualizado durante la
migración produce un 404 —falla ruidosa y segura— en vez de una fuga silenciosa.

## 3. Consecuencias

**El M2M no cambia de comportamiento.** Va contenedor→contenedor por la red Docker
(`http://auth-api:8080/internal/...`); nunca pasó por el Gateway. Verificado:
`grep -c "http://gateway:8080"` sobre `deploy/docker/docker-compose.yml` = 0.

**Coste real de la migración, medido y no estimado:** 11 controllers renombrados y **24 literales de
path** actualizados — 22 en clientes M2M .NET y **2 en Communication (Node)**. Ese cruce .NET↔Node es
lo que hacía peligroso dejar GW-02 como "higiene futura": son strings, `dotnet build` pasa verde con
un path roto.

**Colisiones de path entre servicios distintos son aceptables.** Auth expone
`internal/tenants/{id}/owners` y Tenant expone `internal/tenants/from-onboarding`. No colisionan
porque viven en hosts distintos y el cliente M2M siempre nombra el host.

**Postman.** Las 8 entradas de endpoints internos que iban por `{{UrlBase}}` (el Gateway) ahora
apuntan al puerto directo del servicio vía `{{AuthDirectBase}}`, `{{TenantDirectBase}}`,
`{{SubscriptionDirectBase}}` y `{{PaymentAppDirectBase}}` — el mismo patrón que `{{GrowthDirectBase}}`
ya usaba. Una novena entrada (`GetOnboardingStatus (M2M)`) se borró: apuntaba a un endpoint que no
existe desde que se eliminó `IAuthOnboardingStatusClient`.

## 4. Alternativa descartada: un gateway o listener interno separado

Es lo que recomiendan Microsoft y OWASP cuando hay una superficie M2M grande, y se evaluó. **No
aplica hoy por un motivo verificable, no por prioridad**: cero clientes M2M pasan por el Gateway. Un
listener interno separado no tendría nada que enrutar — sería infraestructura nueva delante de cero
peticiones, con su propia configuración, health checks y modos de fallo.

El control que sí importa hoy es la **aislación de red**: los 19 servicios usan `expose:`, no
`ports:`, así que solo son alcanzables desde la red Docker.

**Cuándo reconsiderarlo:** si el M2M pasara a enrutarse por el Gateway (observabilidad centralizada,
mTLS terminado en un punto) o si los servicios dejaran de compartir red.

## 5. Referencias

- `src/Gateway/TaxVision.Gateway/Middleware/InternalSurfaceGuardMiddleware.cs`
- `deploy/tests/TaxVision.Gateway.Tests/Middleware/InternalSurfaceGuardMiddlewareTests.cs`
- `deploy/tests/TaxVision.BuildingBlocks.Tests/Architecture/InternalRouteConventionFitnessTests.cs`
- `Implementaciones/Documentacion Completa/03_Gateway_Documentacion_Completa.md` §8.4
- OWASP API Security Top 10 2023 — API9:2023 Improper Inventory Management
