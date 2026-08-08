using System.Collections;

namespace ClearVision.Product.Desktop.Observation;

internal sealed class KnownFiniteCollectionAccessor
{
    private readonly IList? _indexed;
    private readonly IEnumerable? _enumerable;

    private KnownFiniteCollectionAccessor(int count, Type? itemType, IList? indexed, IEnumerable? enumerable)
    {
        Count = count;
        ItemType = itemType;
        _indexed = indexed;
        _enumerable = enumerable;
    }

    public int Count { get; }

    public Type? ItemType { get; }

    public bool TryReadPrefix(int maxItems, out IReadOnlyList<object?> items)
    {
        var limit = Math.Min(Count, Math.Max(0, maxItems));
        var result = new List<object?>(limit);
        try
        {
            if (_indexed != null)
            {
                for (var index = 0; index < limit; index++)
                {
                    result.Add(_indexed[index]);
                }

                items = result;
                return true;
            }

            if (_enumerable == null)
            {
                items = [];
                return limit == 0;
            }

            if (limit == 0)
            {
                items = result;
                return true;
            }

            var enumerator = _enumerable.GetEnumerator();
            try
            {
                while (result.Count < limit && enumerator.MoveNext())
                {
                    result.Add(enumerator.Current);
                }
            }
            finally
            {
                (enumerator as IDisposable)?.Dispose();
            }

            items = result;
            return result.Count == limit;
        }
        catch
        {
            items = [];
            return false;
        }
    }

    public static bool TryCreate(object value, out KnownFiniteCollectionAccessor accessor)
    {
        accessor = null!;
        if (value is string || value is IDictionary)
        {
            return false;
        }

        if (value is Array array)
        {
            if (array.Rank != 1)
            {
                return false;
            }

            accessor = new KnownFiniteCollectionAccessor(
                array.Length,
                array.GetType().GetElementType(),
                array,
                array);
            return true;
        }

        if (value is IList list)
        {
            accessor = new KnownFiniteCollectionAccessor(
                list.Count,
                ResolveItemType(value.GetType()),
                list,
                list);
            return true;
        }

        if (value is ICollection collection)
        {
            accessor = new KnownFiniteCollectionAccessor(
                collection.Count,
                ResolveItemType(value.GetType()),
                null,
                collection);
            return true;
        }

        if (value is not IEnumerable enumerable)
        {
            return false;
        }

        var collectionInterface = value.GetType().GetInterfaces()
            .FirstOrDefault(type =>
                type.IsGenericType &&
                (type.GetGenericTypeDefinition() == typeof(ICollection<>) ||
                 type.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>)));
        if (collectionInterface == null)
        {
            return false;
        }

        try
        {
            var count = (int)(collectionInterface.GetProperty(nameof(ICollection<object>.Count))?.GetValue(value) ?? -1);
            if (count < 0)
            {
                return false;
            }

            accessor = new KnownFiniteCollectionAccessor(
                count,
                collectionInterface.GetGenericArguments()[0],
                null,
                enumerable);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Type? ResolveItemType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        return type.GetInterfaces()
            .Concat([type])
            .Where(candidate => candidate.IsGenericType)
            .FirstOrDefault(candidate =>
                candidate.GetGenericTypeDefinition() == typeof(IList<>) ||
                candidate.GetGenericTypeDefinition() == typeof(ICollection<>) ||
                candidate.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>) ||
                candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }
}
