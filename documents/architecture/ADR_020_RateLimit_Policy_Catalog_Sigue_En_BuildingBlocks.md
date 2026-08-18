# ADR-020 — `RateLimitPolicyCatalog` se queda en BuildingBlocks (y con qué disparador se mueve)

**Estado**: Aceptado — 2026-08-08. Decisión de **no hacer** un cambio, con condición de revisión explícita.
**Contexto previo**: [[ADR_019]] (superficie interna) y el split de `BuildingBlocks.Messaging` /
`BuildingBlocks.Authorization` del 2026-08-08.

---

## 1. Contexto

El 2026-08-08 se partió `BuildingBlocks` en ensamblados: `Messaging` (129 archivos) y `Authorization`
(20) salieron del núcleo, que pasó de **209 a 63 archivos**. El criterio no fue el tamaño sino
**churn × alcance** — commits en 3 meses: Messaging 36, Authorization 18, todo el resto junto 27.

`RateLimiting` quedó fuera de ese movimiento pese a ser el segundo folder más grande (35 archivos,
2.855 líneas) y pese a tener **el mismo olor** que motivó los otros dos: conocimiento específico de
cada servicio viviendo en un ensamblado compartido, en 19 archivos
`Catalog/RateLimitPolicyCatalog.{Servicio}.cs`.

Este ADR existe para que esa exclusión sea una decisión con fecha de revisión y no una omisión que
nadie recuerde por qué se hizo.

## 2. Por qué no se partió como los otros dos

**No es principalmente por churn. Es que no se puede sin rediseñar la agregación.**

`RateLimitPolicyCatalog` es una **`static partial class` repartida en 19 archivos** que se
auto-registra por reflexión sobre sus propios campos:

```csharp
private static readonly Lazy<IReadOnlyDictionary<string, RateLimitPolicyDefinition>> ByNameLazy = new(() =>
    typeof(RateLimitPolicyCatalog)
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(f => f.FieldType == typeof(RateLimitPolicyDefinition))
        ...
```

En C# **una `partial class` no puede repartirse entre ensamblados** — es una regla del lenguaje. Con
`Messaging` y `Authorization` el split funcionó porque son tipos independientes; acá los 19 archivos
son trozos de *un mismo tipo*. Mover `RateLimitPolicyCatalog.Notes.cs` a otro ensamblado sencillamente
no compila.

Ese diseño está además justificado en su propio doc-comment: una lista manual de nombres dispararía
CS8601 en cada entrada, porque el analizador de nulabilidad no puede probar el orden de inicialización
de *field initializers* entre archivos de una misma partial (el lenguaje no lo garantiza).

## 3. Decisión

**`RateLimitPolicyCatalog` se queda en el núcleo de `BuildingBlocks`.**

Datos que sostienen la decisión, medidos el 2026-08-08:

| Señal | Valor |
|---|---|
| Commits que tocaron `RateLimiting/` en 3 meses | **6** (Messaging 36, Authorization 18) |
| Archivos de capa Domain que dependen de `ITenantPlanCodeProjection` | **17** |
| Usos de `RateLimitPolicyCatalog` en todo el repo | **11** |

Es código **grande pero estable**, y encima el Domain de cada servicio depende de una parte de él. Un
ensamblado que casi no cambia no cuesta nada aunque lo referencie todo el mundo: lo que duele es el
churn, no el tamaño.

## 4. Cómo se migraría, si el disparador se cumple

Reemplazar «una partial + reflexión sobre sí misma» por **composición por proveedores**, que es el
patrón estándar para catálogos distribuidos:

```csharp
// Núcleo — el contrato
public interface IRateLimitPolicySource
{
    IEnumerable<RateLimitPolicyDefinition> Policies { get; }
}
```

```csharp
// TaxVision.Notes.Application — las políticas viven con su servicio
public sealed class NotesRateLimitPolicies : IRateLimitPolicySource
{
    public IEnumerable<RateLimitPolicyDefinition> Policies => [NotesGet, NotesList, /* ... */];
}
```

`IRateLimitPolicyRegistry` pasa a componer `IEnumerable<IRateLimitPolicySource>` por DI en vez de
reflexionar sobre un tipo.

**Lo que abarata la migración cuando toque:**

- Ya existe la costura: `IRateLimitPolicyRegistry` es la abstracción por la que pasa casi todo.
- Los atributos usan strings (`[RateLimit("notes.f.get")]`), **no** referencian el tipo — los ~180
  endpoints no se tocan.
- Solo hay 11 usos directos del catálogo.

**Lo que cuesta:** la validación de arranque y las fitness functions leen `RateLimitPolicyCatalog.All`
de forma estática; pasarían a resolver el registry. Estimado ~1 día, riesgo bajo pero real.

## 5. Disparador de revisión

Se ejecuta la migración de §4 cuando se cumpla **cualquiera** de estas condiciones:

1. `RateLimiting/` supere **15 commits en un trimestre** (hoy: 6).
2. Se añada un microservicio nuevo — cada alta obliga hoy a tocar un ensamblado compartido.
3. `RateLimiting` empiece a bloquear compilaciones de forma perceptible en el ciclo de desarrollo.

Medir la condición 1 es un comando:

```bash
git log --oneline --since="3 months ago" -- src/BuildingBlocks/RateLimiting | wc -l
```

## 6. Consecuencias

**Se acepta** que dar de alta un servicio nuevo siga obligando a añadir un
`RateLimitPolicyCatalog.{Servicio}.cs` en un ensamblado compartido, y que un cambio de políticas
recompile a todos los que referencian el núcleo — hoy 6 veces por trimestre.

**No se acepta** que esto quede como deuda anónima: este ADR es la traza, y §5 la condición que la
convierte en trabajo.

**Lo que NO cambia esta decisión:** el tiempo de build de imágenes Docker. Eso depende del contexto de
build y de los filtros de ruta del CI, no del grafo de ensamblados.

## 7. Alternativa descartada: partir solo los `Catalog/*.cs` dejando el resto

Se evaluó mover únicamente los 19 archivos de catálogo. **No es posible por el mismo motivo del §2**:
son `partial` del mismo tipo que el agregador. Habría que romper la partial primero — que es
exactamente la migración del §4, sin ahorro alguno.

## 8. Referencias

- `src/BuildingBlocks/RateLimiting/Catalog/RateLimitPolicyCatalog.cs` — el agregador por reflexión
- `src/BuildingBlocks/RateLimiting/Catalog/IRateLimitPolicyRegistry.cs` — la costura ya existente
- `documents/RateLimit/Plan_Implementacion_Fases.md` — historia del catálogo
- `documents/architecture/ADR_017_RateLimit_Layers.md` — las capas de rate limiting
