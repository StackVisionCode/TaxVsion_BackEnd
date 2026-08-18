namespace TaxVision.Tasks.Domain.Dependencies;

// Un solo valor por ahora; la columna ya existe para que sumar los otros sea dato, no esquema.
public enum DependencyType
{
    FinishToStart = 1,
}
