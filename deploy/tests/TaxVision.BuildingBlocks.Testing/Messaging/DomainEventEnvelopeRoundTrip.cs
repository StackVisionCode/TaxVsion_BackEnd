using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using BuildingBlocks.Domain;
using Wolverine;

namespace TaxVision.BuildingBlocks.Testing.Messaging;

/// <summary>
/// Viaje de ida y vuelta de un <see cref="IDomainEvent"/> por el serializador del sobre de Wolverine. La primera
/// entrega va en memoria; tras un reinicio el evento se relee de <c>wolverine_incoming_envelopes</c>. Un tipo sin
/// constructor público, o una propiedad sin setter fuera del constructor, pasa en memoria y falla o cambia al releer.
/// La muestra se arma campo por campo, sin pasar por constructores, y se compara campo por campo: la igualdad de un
/// record no sirve con colecciones.
/// </summary>
public static class DomainEventEnvelopeRoundTrip
{
    private const int MaxDepth = 8;

    /// <summary>Eventos concretos de <paramref name="candidates"/> que no vuelven iguales, con el motivo.</summary>
    public static IReadOnlyList<string> FindEventsThatDoNotSurvive(IEnumerable<Type> candidates)
    {
        var serializer = new WolverineOptions().DefaultSerializer;

        return candidates
            .Where(t => !t.IsInterface && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .Where(t => typeof(IDomainEvent).IsAssignableFrom(t))
            .Distinct()
            .OrderBy(t => t.FullName)
            .Select(t => (Type: t, Problem: FindProblem(t, serializer)))
            .Where(r => r.Problem is not null)
            .Select(r => $"{r.Type.FullName}: {r.Problem}")
            .ToList();
    }

    /// <summary>Instancia de <paramref name="type"/> con todos sus campos llenos, cada valor distinto del anterior.</summary>
    public static object BuildSample(Type type) => new SampleBuilder().Build(type, 0)!;

    private static string? FindProblem(Type eventType, Wolverine.Runtime.Serialization.IMessageSerializer serializer)
    {
        object sample;
        try
        {
            sample = BuildSample(eventType);
        }
        catch (NotSupportedException ex)
        {
            return $"no se pudo armar la muestra: {ex.Message}";
        }

        object? reread;
        try
        {
            var envelope = new Envelope { Data = serializer.WriteMessage(sample) };
            reread = serializer.ReadFromData(eventType, envelope);
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message.Split('\n')[0].Trim()}";
        }

        return FindDifference(eventType.Name, sample, reread);
    }

    private static string? FindDifference(string path, object? expected, object? actual)
    {
        if (expected is null || actual is null)
        {
            return expected is null && actual is null ? null : $"{path} cambió: {Show(expected)} → {Show(actual)}";
        }

        var type = expected.GetType();
        if (IsLeaf(type))
        {
            return expected.Equals(actual) ? null : $"{path} cambió: {Show(expected)} → {Show(actual)}";
        }

        if (expected is IDictionary expectedMap && actual is IDictionary actualMap)
        {
            return FindDictionaryDifference(path, expectedMap, actualMap);
        }

        if (expected is IEnumerable expectedItems && actual is IEnumerable actualItems)
        {
            return FindSequenceDifference(path, expectedItems, actualItems);
        }

        if (type != actual.GetType())
        {
            return $"{path} cambió de tipo: {type.Name} → {actual.GetType().Name}";
        }

        return InstanceFields(type)
            .Select(f => FindDifference($"{path}.{FieldName(f)}", f.GetValue(expected), f.GetValue(actual)))
            .FirstOrDefault(d => d is not null);
    }

    private static string? FindDictionaryDifference(string path, IDictionary expected, IDictionary actual)
    {
        if (expected.Count != actual.Count)
        {
            return $"{path} cambió de tamaño: {expected.Count} → {actual.Count}";
        }

        foreach (DictionaryEntry entry in expected)
        {
            if (!actual.Contains(entry.Key))
            {
                return $"{path} perdió la clave {entry.Key}";
            }

            if (FindDifference($"{path}[{entry.Key}]", entry.Value, actual[entry.Key]) is { } difference)
            {
                return difference;
            }
        }

        return null;
    }

    private static string? FindSequenceDifference(string path, IEnumerable expected, IEnumerable actual)
    {
        var left = expected.Cast<object?>().ToList();
        var right = actual.Cast<object?>().ToList();
        if (left.Count != right.Count)
        {
            return $"{path} cambió de tamaño: {left.Count} → {right.Count}";
        }

        return left.Select((item, i) => FindDifference($"{path}[{i}]", item, right[i]))
            .FirstOrDefault(d => d is not null);
    }

    private static string Show(object? value) => value?.ToString() ?? "null";

    private static string FieldName(FieldInfo field) =>
        field.Name.StartsWith('<') ? field.Name[1..field.Name.IndexOf('>')] : field.Name;

    private static bool IsLeaf(Type type) =>
        type.IsPrimitive
        || type.IsEnum
        || type == typeof(string)
        || type == typeof(decimal)
        || type == typeof(Guid)
        || type == typeof(DateTime)
        || type == typeof(DateTimeOffset)
        || type == typeof(DateOnly)
        || type == typeof(TimeOnly)
        || type == typeof(TimeSpan)
        || type == typeof(Uri);

    // Incluye los campos privados de las clases base.
    private static IEnumerable<FieldInfo> InstanceFields(Type type)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (
                var field in current.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
                )
            )
            {
                yield return field;
            }
        }
    }

    private sealed class SampleBuilder
    {
        private static readonly DateTime Epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private int _seed;

        public object? Build(Type type, int depth)
        {
            if (depth > MaxDepth)
            {
                throw new NotSupportedException($"{type.Name} anida demasiado");
            }

            var seed = ++_seed;
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying is not null)
            {
                return Build(underlying, depth);
            }

            return BuildLeaf(type, seed) ?? BuildCollection(type, depth) ?? BuildObject(type, depth);
        }

        private static object? BuildLeaf(Type type, int seed) =>
            type switch
            {
                _ when type == typeof(string) || type == typeof(object) => $"sample-{seed}",
                _ when type == typeof(bool) => true,
                _ when type == typeof(char) => (char)('a' + seed % 26),
                _ when type == typeof(byte) => (byte)seed,
                _ when type == typeof(short) => (short)seed,
                _ when type == typeof(int) => seed,
                _ when type == typeof(long) => (long)seed,
                _ when type == typeof(float) => seed + 0.5f,
                _ when type == typeof(double) => seed + 0.5d,
                _ when type == typeof(decimal) => seed + 0.25m,
                _ when type == typeof(Guid) => new Guid(seed, 0, 0, new byte[8]),
                _ when type == typeof(DateTime) => Epoch.AddMinutes(seed),
                _ when type == typeof(DateTimeOffset) => new DateTimeOffset(Epoch.AddMinutes(seed)),
                _ when type == typeof(DateOnly) => DateOnly.FromDateTime(Epoch.AddDays(seed)),
                _ when type == typeof(TimeOnly) => TimeOnly.FromDateTime(Epoch.AddMinutes(seed)),
                _ when type == typeof(TimeSpan) => TimeSpan.FromMinutes(seed),
                _ when type == typeof(Uri) => new Uri($"https://sample.test/{seed}"),
                _ when type.IsEnum => Enum.GetValues(type).Cast<object>().Last(),
                _ => null,
            };

        private object? BuildCollection(Type type, int depth)
        {
            if (type.IsArray)
            {
                var element = type.GetElementType()!;
                var array = Array.CreateInstance(element, 2);
                array.SetValue(Build(element, depth + 1), 0);
                array.SetValue(Build(element, depth + 1), 1);
                return array;
            }

            if (!type.IsGenericType)
            {
                return null;
            }

            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();

            if (
                definition == typeof(Dictionary<,>)
                || definition == typeof(IDictionary<,>)
                || definition == typeof(IReadOnlyDictionary<,>)
            )
            {
                var map = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(arguments))!;
                map.Add(Build(arguments[0], depth + 1)!, Build(arguments[1], depth + 1));
                map.Add(Build(arguments[0], depth + 1)!, Build(arguments[1], depth + 1));
                return map;
            }

            if (
                definition == typeof(List<>)
                || definition == typeof(IList<>)
                || definition == typeof(ICollection<>)
                || definition == typeof(IEnumerable<>)
                || definition == typeof(IReadOnlyList<>)
                || definition == typeof(IReadOnlyCollection<>)
            )
            {
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(arguments))!;
                list.Add(Build(arguments[0], depth + 1));
                list.Add(Build(arguments[0], depth + 1));
                return list;
            }

            if (definition == typeof(HashSet<>) || definition == typeof(ISet<>) || definition == typeof(IReadOnlySet<>))
            {
                var set = Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(arguments))!;
                var add = set.GetType().GetMethod(nameof(HashSet<object>.Add))!;
                add.Invoke(set, [Build(arguments[0], depth + 1)]);
                add.Invoke(set, [Build(arguments[0], depth + 1)]);
                return set;
            }

            return null;
        }

        private object BuildObject(Type type, int depth)
        {
            if (type.IsInterface || type.IsAbstract || typeof(Delegate).IsAssignableFrom(type))
            {
                throw new NotSupportedException($"{type.Name} no tiene un tipo concreto");
            }

            var instance = RuntimeHelpers.GetUninitializedObject(type);
            foreach (var field in InstanceFields(type))
            {
                field.SetValue(instance, Build(field.FieldType, depth + 1));
            }

            return instance;
        }
    }
}
