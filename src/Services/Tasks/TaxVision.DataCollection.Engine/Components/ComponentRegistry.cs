namespace TaxVision.DataCollection.Engine.Components;

public sealed class ComponentRegistry
{
    private readonly IReadOnlyDictionary<string, IComponentType> _types;
    private readonly IReadOnlyCollection<IComponentType> _all;

    public ComponentRegistry()
    {
        IComponentType[] types =
        [
            new HeadingComponentType(),
            new ParagraphComponentType(),
            new CalloutComponentType(),
            new DividerComponentType(),
            new SpacerComponentType(),
            new ConsentComponentType(),
            new SignatureStepComponentType(),
            new TextComponentType(),
            new TextareaComponentType(),
            new NumberComponentType(),
            new CurrencyComponentType(),
            new DateComponentType(),
            new EmailComponentType(),
            new PhoneComponentType(),
            new SelectComponentType(),
            new RadioComponentType(),
            new CheckboxGroupComponentType(),
            new YesNoComponentType(),
            new CheckboxComponentType(),
            new FileComponentType(),
            new AddressComponentType(),
            new TaxIdComponentType(),
        ];
        _all = types;
        _types = types.ToDictionary(item => item.Name, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<IComponentType> All => _all;

    public bool TryGet(string name, out IComponentType componentType) => _types.TryGetValue(name, out componentType!);
}
