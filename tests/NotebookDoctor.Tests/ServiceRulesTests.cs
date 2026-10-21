namespace NotebookDoctor.Tests;

public class ServiceRulesTests
{
    private const string Win = @"C:\Windows";
    private static ServiceInfo S(string name, string path, int start = 2, string? display = null) => new(name, display ?? name, path, start);

    [Theory]
    [InlineData(@"\SystemRoot\System32\svchost.exe -k netsvcs", @"C:\Windows\System32\svchost.exe")]
    [InlineData(@"system32\drivers\x.sys", @"C:\Windows\system32\drivers\x.sys")]
    [InlineData(@"""C:\Program Files\Google\GoogleUpdater\updater.exe"" --system --windows-service", @"C:\Program Files\Google\GoogleUpdater\updater.exe")]
    [InlineData(@"\??\C:\Tools\svc.exe", @"C:\Tools\svc.exe")]
    public void Resolve_o_executavel(string image, string expected) =>
        Assert.Equal(expected, ServiceRules.ExecutablePath(image, Win));

    [Fact]
    public void Servicos_do_Windows_nunca_sao_candidatos()
    {
        var all = new[]
        {
            S("Dnscache", @"C:\WINDOWS\system32\svchost.exe -k NetworkService"),
            S("wuauserv", @"\SystemRoot\System32\svchost.exe -k netsvcs"),
            S("GoogleUpdaterService", @"""C:\Program Files\Google\GoogleUpdater\updater.exe"" --system"),
        };
        var c = ServiceRules.Candidates(all, Win);
        Assert.Equal("GoogleUpdaterService", Assert.Single(c).Service.Name);
    }

    [Fact]
    public void So_entram_os_que_iniciam_sozinhos()
    {
        var all = new[] { S("A", @"C:\App\a.exe", start: 3), S("B", @"C:\App\b.exe", start: 4), S("C", @"C:\App\c.exe", start: 2) };
        Assert.Equal("C", Assert.Single(ServiceRules.Candidates(all, Win)).Service.Name);
    }

    [Theory]
    [InlineData("AdobeARMservice", "Adobe Acrobat Update Service", ServiceAdvice.CanBeManual)]
    [InlineData("edgeupdate", "Microsoft Edge Update Service", ServiceAdvice.CanBeManual)]
    [InlineData("AvastSvc", "Avast Antivirus", ServiceAdvice.Keep)]
    [InlineData("RtkAudioUniversalService", "Realtek Audio Universal Service", ServiceAdvice.Keep)]
    [InlineData("NVDisplay.ContainerLocalSystem", "NVIDIA Display Container LS", ServiceAdvice.Keep)]
    [InlineData("SomeVendorSvc", "Some Vendor Service", ServiceAdvice.Review)]
    public void Classifica(string name, string display, ServiceAdvice expected) =>
        Assert.Equal(expected, ServiceRules.Classify(S(name, @"C:\Program Files\X\x.exe", display: display)).Advice);

    [Fact]
    public void Ordena_do_mais_recomendavel_ao_menos()
    {
        var all = new[]
        {
            S("AvastSvc", @"C:\Program Files\Avast\a.exe", display: "Avast Antivirus"),
            S("Other", @"C:\Program Files\O\o.exe", display: "Other Service"),
            S("gupdate", @"C:\Program Files\Google\Update\GoogleUpdate.exe", display: "Google Update"),
        };
        Assert.Equal(["gupdate", "Other", "AvastSvc"], ServiceRules.Candidates(all, Win).Select(c => c.Service.Name));
    }
}

public class VirtualDiskTests
{
    [Fact]
    public void Disco_virtual_grande_gera_aviso_com_local()
    {
        var h = Make.Health() with { VirtualDisks = [new VirtualDisk("Ubuntu", @"C:\x\ext4.vhdx", 40L << 30), new VirtualDisk("Debian", @"C:\y\ext4.vhdx", 2L << 30)] };
        var f = Assert.Single(Diagnosis.Evaluate(Make.Minute(Make.Snap()), h, uptime: Make.FreshBoot));
        Assert.Equal("Disco virtual do Ubuntu ocupa 40,0 GB", f.Title);
        Assert.Equal("open-folder", f.FixId);
        Assert.Equal(@"C:\x\ext4.vhdx", f.FixArg);
        Assert.True(f.HasWhy);
    }
}
