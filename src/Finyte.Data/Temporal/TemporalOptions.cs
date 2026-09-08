namespace Finyte.Data.Temporal;

public sealed class TemporalOptions
{
    public const string SectionName = "Temporal";

    public bool Enabled { get; set; } = true;

    public string Address { get; set; } = "localhost:7233";

    public string Namespace { get; set; } = "default";

    public string TaskQueue { get; set; } = "finyte-background";
}
