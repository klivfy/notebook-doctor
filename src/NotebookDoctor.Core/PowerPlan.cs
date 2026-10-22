namespace NotebookDoctor.Services;

public sealed record PowerPlan(string Guid, string Name, bool Active)
{
    public override string ToString() => Name;
}
