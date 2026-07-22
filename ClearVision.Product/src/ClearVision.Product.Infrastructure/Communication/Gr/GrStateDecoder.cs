namespace ClearVision.Product.Infrastructure.Communication.Gr;

public sealed class GrStateDecoder
{
    private readonly GrRegisterMapCatalog _catalog;

    public GrStateDecoder(GrRegisterMapCatalog catalog)
    {
        _catalog = catalog;
    }

    public IReadOnlyList<GrDecodedRegister> Decode(int startAddress, IReadOnlyList<ushort> values)
    {
        var definitions = _catalog.GetTemplate().Registers.ToDictionary(item => item.Address);
        return values.Select((value, index) =>
        {
            var address = startAddress + index;
            definitions.TryGetValue(address, out var definition);
            return new GrDecodedRegister(
                address,
                definition?.Key ?? $"register{address}",
                definition?.DisplayName ?? $"Register {address}",
                value,
                DecodeValue(definition, value));
        }).ToArray();
    }

    private static object DecodeValue(GrRegisterDefinition? definition, ushort value) =>
        definition?.ValueType switch
        {
            "boolean" => value != 0,
            "enum" when definition.EnumValues.TryGetValue(value.ToString(), out var label) => label,
            _ => value
        };
}

public sealed record GrDecodedRegister(
    int Address,
    string Key,
    string DisplayName,
    ushort RawValue,
    object Value);
