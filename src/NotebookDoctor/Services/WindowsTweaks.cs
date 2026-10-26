using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace NotebookDoctor.Services;

/// <summary>Um ajuste do Windows como aparece na seção Ajustes: estado atual, por que importa e o que muda.</summary>
public sealed record Tweak(string Id, string Title, string Current, string Why, string ActionLabel, string Preview,
                           bool NeedsAdmin, bool Recommended, bool CanApply)
{
    public string Badge => Loc.T(!CanApply ? "Nada a fazer" : Recommended ? "Recomendado" : "Opcional");
    public string AdminNote => NeedsAdmin && !AppInfo.IsAdmin ? Loc.T("Exige o modo administrador") : "";

    // U1: Title e ActionLabel ficam em português (Apply decide pelo rótulo); a tela mostra estes
    public string TitleText => Loc.T(Title);
    public string ActionText => Loc.T(ActionLabel);
    public string SimulateLabel => Loc.T("Simular");
}

/// <summary>
/// A31 hibernação, A32 paginação, A33 efeitos visuais, A34 apps em segundo plano, A35 Inicialização Rápida.
/// Cada um lê o estado, descreve exatamente o que vai mudar (A38) e guarda o valor anterior para o "Desfazer" em Ações.
/// </summary>
public static class WindowsTweaks
{
    private const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";
    private const string MemKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
    private const string BgKey = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";
    private const string SearchKey = @"Software\Microsoft\Windows\CurrentVersion\Search";
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static string SysRoot => Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

    /// <summary>Tamanho de um arquivo oculto do sistema na raiz (hiberfil.sys, pagefile.sys) sem abri-lo.</summary>
    private static long RootFileSize(string name)
    {
        try
        {
            var opts = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true };
            return new DirectoryInfo(SysRoot).EnumerateFiles(name, opts).FirstOrDefault()?.Length ?? 0;
        }
        catch { return 0; }
    }

    private static int? ReadDword(RegistryKey root, string path, string name)
    {
        try { using var k = root.OpenSubKey(path); return k?.GetValue(name) is int v ? v : null; } catch { return null; }
    }

    public static List<Tweak> Read(double ramGB, double diskFreePct)
    {
        var list = new List<Tweak>();

        // ---- A31 Hibernação ----
        long hiber = RootFileSize("hiberfil.sys");
        bool hiberOn = hiber > 0;
        list.Add(new Tweak("hibernate", "Hibernação",
            hiberOn ? Loc.F("Ligada · hiberfil.sys ocupa {0}", Cleaner.FormatBytes(hiber)) : Loc.T("Desligada (sem hiberfil.sys)"),
            Loc.T("O Windows reserva no disco um arquivo do tamanho de boa parte da RAM para poder hibernar. Quem só usa Suspender não precisa dele; desligar devolve esse espaço. Desligar também desativa a Inicialização Rápida."),
            hiberOn ? "Desligar hibernação" : "Religar hibernação",
            hiberOn ? Loc.F("Executa \"powercfg /hibernate off\". Apaga {0}hiberfil.sys ({1}), tira \"Hibernar\" do menu Energia e desliga a Inicialização Rápida.", SysRoot, Cleaner.FormatBytes(hiber))
                    : Loc.T("Executa \"powercfg /hibernate on\". Recria o hiberfil.sys e volta a permitir Hibernar e a Inicialização Rápida."),
            NeedsAdmin: true, Recommended: hiberOn && diskFreePct < 20, CanApply: true));

        // ---- A35 Inicialização Rápida ----
        int? fast = ReadDword(Registry.LocalMachine, PowerKey, "HiberbootEnabled");
        bool fastOn = fast == 1 && hiberOn;
        double upDays = TimeSpan.FromMilliseconds(Environment.TickCount64).TotalDays;
        list.Add(new Tweak("faststartup", "Inicialização Rápida",
            !hiberOn ? Loc.T("Inativa (precisa da hibernação ligada)") : fastOn ? Loc.F("Ligada · o Windows está há {0:0.#} dias sem reiniciar de verdade", upDays) : Loc.T("Desligada"),
            Loc.T("Com ela, \"Desligar\" na verdade hiberna o núcleo do Windows: o boot fica alguns segundos mais rápido, mas problemas e memória acumulada não são zerados, e drivers e atualizações às vezes não se aplicam. Desligar faz cada desligamento ser completo."),
            fastOn ? "Desligar Inicialização Rápida" : "Ligar Inicialização Rápida",
            Loc.F(@"Altera HKLM\{0}\HiberbootEnabled de {1} para {2}. Vale a partir do próximo desligamento.", PowerKey, fast ?? 0, fastOn ? 0 : 1),
            NeedsAdmin: true, Recommended: fastOn && upDays >= 3, CanApply: hiberOn));

        // ---- A32 Arquivo de paginação ----
        string[] paging = ReadPaging();
        bool auto = paging.Any(p => p.StartsWith("?:", StringComparison.Ordinal));
        bool none = paging.Length == 0 || paging.All(string.IsNullOrWhiteSpace);
        long pageSize = RootFileSize("pagefile.sys");
        string pageNow = none ? Loc.T("Desativado (sem arquivo de paginação)")
            : auto ? Loc.F("Gerenciado pelo Windows · {0} agora", Cleaner.FormatBytes(pageSize))
            : Loc.F("Tamanho fixo ({0}) · {1} agora", string.Join("; ", paging), Cleaner.FormatBytes(pageSize));
        list.Add(new Tweak("pagefile", "Arquivo de paginação (memória virtual)", pageNow,
            Loc.F("Quando a RAM ({0}) enche, o Windows usa o disco como extensão. Sem ele, ou com tamanho fixo pequeno, programas podem fechar sozinhos por falta de memória. O recomendado é deixar o Windows gerenciar o tamanho.", Units.Gb(ramGB, "0")),
            auto ? "Já recomendado" : "Deixar o Windows gerenciar",
            Loc.F(@"Altera HKLM\{0}\PagingFiles para ""?:\pagefile.sys"" (gerenciado pelo sistema). Exige reiniciar o computador.", MemKey),
            NeedsAdmin: true, Recommended: !auto, CanApply: !auto));

        // ---- A33 Efeitos visuais ----
        var fx = VisualEffects.Read();
        int onCount = fx.Values.Count(v => v);
        list.Add(new Tweak("visualfx", "Efeitos visuais",
            onCount == 0 ? Loc.T("No modo \"melhor desempenho\" (efeitos desligados)") : Loc.F("{0} de {1} efeitos ligados (animações, sombras, transparência)", onCount, fx.Count),
            Loc.T("Animações, sombras e transparência usam a placa de vídeo integrada e a CPU. Em notebooks mais simples, desligar deixa a interface mais ágil. O texto continua suavizado."),
            onCount == 0 ? "Já no melhor desempenho" : "Usar melhor desempenho",
            Loc.F("Desliga: {0}. Vale na hora, só para seu usuário.", string.Join(", ", fx.Where(kv => kv.Value).Select(kv => Loc.T(VisualEffects.Label(kv.Key))))),
            NeedsAdmin: false, Recommended: false, CanApply: onCount > 0));

        // ---- A34 Apps em segundo plano ----
        bool bgOff = ReadDword(Registry.CurrentUser, BgKey, "GlobalUserDisabled") == 1 || ReadDword(Registry.CurrentUser, SearchKey, "BackgroundAppGlobalToggle") == 0;
        list.Add(new Tweak("bgapps", "Apps da Microsoft Store em segundo plano",
            Loc.T(bgOff ? "Bloqueados" : "Permitidos"),
            Loc.T("Apps da Store (Notícias, Fotos, Clima, Xbox…) podem rodar mesmo fechados para atualizar conteúdo. Bloquear economiza CPU, memória e bateria. Esses apps podem deixar de mostrar notificações até serem abertos. Programas comuns, como o Chrome, não são afetados."),
            bgOff ? "Permitir de novo" : "Bloquear segundo plano",
            bgOff ? Loc.F(@"Remove o bloqueio em HKCU\{0}\GlobalUserDisabled e HKCU\{1}\BackgroundAppGlobalToggle.", BgKey, SearchKey)
                  : Loc.F(@"Grava HKCU\{0}\GlobalUserDisabled = 1 e HKCU\{1}\BackgroundAppGlobalToggle = 0. Só para seu usuário.", BgKey, SearchKey),
            NeedsAdmin: false, Recommended: false, CanApply: true));

        return list;
    }

    private static string[] ReadPaging()
    {
        try { using var k = Registry.LocalMachine.OpenSubKey(MemKey); return k?.GetValue("PagingFiles") as string[] ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>Aplica o ajuste e devolve (mensagem, estado anterior para o desfazer). Lança com mensagem em português se falhar.</summary>
    public static async Task<(string Message, string Previous)> ApplyAsync(Tweak t)
    {
        switch (t.Id)
        {
            case "hibernate":
            {
                bool turningOff = t.ActionLabel.StartsWith("Desligar");
                int code = await SystemIntegrity.RunAsync("powercfg.exe", turningOff ? "/hibernate off" : "/hibernate on", _ => { }, CancellationToken.None);
                if (code != 0) throw new InvalidOperationException($"O powercfg terminou com código {code}.");
                return (turningOff ? "Hibernação desligada; o espaço do hiberfil.sys foi liberado." : "Hibernação religada.", turningOff ? "on" : "off");
            }
            case "faststartup":
            {
                int prev = ReadDword(Registry.LocalMachine, PowerKey, "HiberbootEnabled") ?? 1;
                using var k = Registry.LocalMachine.CreateSubKey(PowerKey, writable: true);
                k.SetValue("HiberbootEnabled", prev == 1 ? 0 : 1, RegistryValueKind.DWord);
                return (prev == 1 ? "Inicialização Rápida desligada. O próximo desligamento será completo." : "Inicialização Rápida ligada.", prev.ToString());
            }
            case "pagefile":
            {
                string[] prev = ReadPaging();
                using var k = Registry.LocalMachine.CreateSubKey(MemKey, writable: true);
                k.SetValue("PagingFiles", new[] { @"?:\pagefile.sys" }, RegistryValueKind.MultiString);
                return ("O Windows passa a gerenciar o arquivo de paginação depois de reiniciar.", JsonSerializer.Serialize(prev));
            }
            case "visualfx":
            {
                var prev = VisualEffects.Read();
                VisualEffects.Apply(prev.ToDictionary(kv => kv.Key, _ => false));
                return ("Efeitos visuais no modo melhor desempenho.", JsonSerializer.Serialize(prev));
            }
            case "bgapps":
            {
                var prev = new Dictionary<string, int?>
                {
                    ["g"] = ReadDword(Registry.CurrentUser, BgKey, "GlobalUserDisabled"),
                    ["s"] = ReadDword(Registry.CurrentUser, SearchKey, "BackgroundAppGlobalToggle"),
                };
                bool block = !t.ActionLabel.StartsWith("Permitir");
                SetBg(block ? 1 : null, block ? 0 : null);
                return (block ? "Apps da Store bloqueados em segundo plano." : "Apps da Store podem rodar em segundo plano de novo.", JsonSerializer.Serialize(prev));
            }
        }
        throw new InvalidOperationException("Ajuste desconhecido.");
    }

    private static void SetBg(int? global, int? toggle)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(BgKey, writable: true))
            if (global is { } g) k.SetValue("GlobalUserDisabled", g, RegistryValueKind.DWord); else k.DeleteValue("GlobalUserDisabled", false);
        using (var k = Registry.CurrentUser.CreateSubKey(SearchKey, writable: true))
            if (toggle is { } t) k.SetValue("BackgroundAppGlobalToggle", t, RegistryValueKind.DWord); else k.DeleteValue("BackgroundAppGlobalToggle", false);
    }

    /// <summary>Volta ao estado guardado em <paramref name="previous"/>. Usado pelo "Desfazer" da seção Ações.</summary>
    public static string Undo(string id, string previous)
    {
        switch (id)
        {
            case "hibernate":
                var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powercfg.exe", previous == "on" ? "/hibernate on" : "/hibernate off") { CreateNoWindow = true, UseShellExecute = false });
                p?.WaitForExit(15000);
                return p?.ExitCode == 0 ? "Hibernação restaurada." : "O Windows recusou (abra o app como administrador e tente de novo).";
            case "faststartup":
                using (var k = Registry.LocalMachine.CreateSubKey(PowerKey, writable: true))
                    k.SetValue("HiberbootEnabled", int.Parse(previous), RegistryValueKind.DWord);
                return "Inicialização Rápida restaurada.";
            case "pagefile":
                using (var k = Registry.LocalMachine.CreateSubKey(MemKey, writable: true))
                    k.SetValue("PagingFiles", JsonSerializer.Deserialize<string[]>(previous) ?? Array.Empty<string>(), RegistryValueKind.MultiString);
                return "Configuração anterior do arquivo de paginação restaurada (vale depois de reiniciar).";
            case "visualfx":
                VisualEffects.Apply(JsonSerializer.Deserialize<Dictionary<string, bool>>(previous) ?? new());
                return "Efeitos visuais restaurados.";
            case "bgapps":
                var prev = JsonSerializer.Deserialize<Dictionary<string, int?>>(previous) ?? new();
                SetBg(prev.GetValueOrDefault("g"), prev.GetValueOrDefault("s"));
                return "Configuração de apps em segundo plano restaurada.";
        }
        return "Ajuste desconhecido.";
    }
}

/// <summary>A33: efeitos visuais via SystemParametersInfo (o mesmo que "Opções de desempenho" do Windows faz) e transparência.</summary>
public static class VisualEffects
{
    // Pares GET/SET do SystemParametersInfo; o SET recebe o valor em pvParam
    private static readonly (string Key, uint Get, uint Set, string Label)[] Bools =
    [
        ("clientanim", 0x1042, 0x1043, "animações dentro das janelas"),
        ("menuanim", 0x1002, 0x1003, "animação de menus"),
        ("tooltipanim", 0x1016, 0x1017, "animação de dicas"),
        ("comboanim", 0x1004, 0x1005, "animação de listas suspensas"),
        ("smoothscroll", 0x1006, 0x1007, "rolagem suave de listas"),
        ("dropshadow", 0x1024, 0x1025, "sombras das janelas"),
        ("cursorshadow", 0x101A, 0x101B, "sombra do ponteiro"),
    ];

    public static string Label(string key) => key switch
    {
        "minanim" => "animação ao minimizar e maximizar",
        "transparency" => "transparência",
        _ => Bools.FirstOrDefault(b => b.Key == key).Label ?? key,
    };

    [StructLayout(LayoutKind.Sequential)] private struct AnimationInfo { public uint cbSize; public int iMinAnimate; }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint action, uint uiParam, ref int pv, uint winIni);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint action, uint uiParam, IntPtr pv, uint winIni);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint action, uint uiParam, ref AnimationInfo pv, uint winIni);

    private const uint SPI_GETANIMATION = 0x0048, SPI_SETANIMATION = 0x0049, SPIF_UPDATEINIFILE = 1, SPIF_SENDCHANGE = 2;
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static Dictionary<string, bool> Read()
    {
        var d = new Dictionary<string, bool>();
        foreach (var b in Bools)
        {
            int v = 0;
            if (SystemParametersInfo(b.Get, 0, ref v, 0)) d[b.Key] = v != 0;
        }
        var ai = new AnimationInfo { cbSize = (uint)Marshal.SizeOf<AnimationInfo>() };
        if (SystemParametersInfo(SPI_GETANIMATION, ai.cbSize, ref ai, 0)) d["minanim"] = ai.iMinAnimate != 0;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            d["transparency"] = k?.GetValue("EnableTransparency") is not int t || t != 0;
        }
        catch { }
        return d;
    }

    public static void Apply(Dictionary<string, bool> wanted)
    {
        foreach (var b in Bools)
            if (wanted.TryGetValue(b.Key, out bool on))
                SystemParametersInfo(b.Set, 0, on ? (IntPtr)1 : IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        if (wanted.TryGetValue("minanim", out bool min))
        {
            var ai = new AnimationInfo { cbSize = (uint)Marshal.SizeOf<AnimationInfo>(), iMinAnimate = min ? 1 : 0 };
            SystemParametersInfo(SPI_SETANIMATION, ai.cbSize, ref ai, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }
        if (wanted.TryGetValue("transparency", out bool tr))
        {
            try { using var k = Registry.CurrentUser.CreateSubKey(PersonalizeKey, writable: true); k.SetValue("EnableTransparency", tr ? 1 : 0, RegistryValueKind.DWord); }
            catch { }
        }
    }
}
