namespace DigitalBrain.Core;

[GenerateSerializer]
public record TaskId([property: Id(0)] string Value)
{
    public static implicit operator string(TaskId id) => id.Value;
    public static implicit operator TaskId(string value) => new(value);
    public override string ToString() => Value;
}