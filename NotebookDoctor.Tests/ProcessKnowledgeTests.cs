using NotebookDoctor.Services;

namespace NotebookDoctor.Tests;

public class ProcessKnowledgeTests
{
    [Theory]
    [InlineData("chrome", Safety.Safe)]
    [InlineData("CHROME", Safety.Safe)]       // sem diferenciar maiúsculas
    [InlineData("explorer", Safety.Never)]
    [InlineData("csrss", Safety.Never)]
    public void KnownProcesses(string name, Safety expected) => Assert.Equal(expected, ProcessKnowledge.Describe(name).Safety);

    [Fact]
    public void UnknownProcess_IsUnknown() => Assert.Equal(Safety.Unknown, ProcessKnowledge.Describe("programa-que-nao-existe-xyz").Safety);

    [Fact]
    public void EveryKnownProcess_HasDescription()
    {
        foreach (var name in new[] { "chrome", "csrss", "explorer" })
            Assert.False(string.IsNullOrWhiteSpace(ProcessKnowledge.Describe(name).Description), name);
    }
}
