using NotebookDoctor.Services;

namespace NotebookDoctor.Tests;

/// <summary>Fase 11, leva 11.1: bateria em watts, previsão, calor, Modo Colo e Modo sobrevivência.</summary>
public class Fase11Tests
{
    private static BatteryReading B(double rateW, bool onAc = false, int pct = 50, bool charging = false, double remaining = 30) =>
        new(Make.T0, onAc, charging, rateW, remaining, 50, 57, pct);

    [Fact]
    public void Taxa_em_watts_com_sentido()
    {
        Assert.Equal(Loc.F("{0:0.0} W saindo", 13.8), BatteryRules.RateText(B(-13.8)));
        Assert.Equal(Loc.F("{0:0.0} W entrando", 45.0), BatteryRules.RateText(B(45, onAc: true, charging: true)));
        Assert.Equal("", BatteryRules.RateText(B(0)));
    }

    [Fact]
    public void Carregador_lento_ou_que_nao_da_conta()
    {
        var slow = Enumerable.Repeat(B(6, onAc: true, pct: 40, charging: true), 4).ToList();
        Assert.Equal("Carregador lento", BatteryRules.SlowCharger(slow)!.Title);
        var losing = Enumerable.Repeat(B(-4, onAc: true, pct: 40), 4).ToList();
        Assert.Equal("A bateria descarrega mesmo na tomada", BatteryRules.SlowCharger(losing)!.Title);
    }

    [Fact]
    public void Carregador_bom_bateria_cheia_ou_poucas_leituras_nao_acusam()
    {
        Assert.Null(BatteryRules.SlowCharger(Enumerable.Repeat(B(45, onAc: true, pct: 40, charging: true), 4).ToList()));
        Assert.Null(BatteryRules.SlowCharger(Enumerable.Repeat(B(3, onAc: true, pct: 95, charging: true), 4).ToList()));  // perto de cheia, carrega devagar mesmo
        Assert.Null(BatteryRules.SlowCharger(Enumerable.Repeat(B(6, onAc: true, pct: 40, charging: true), 3).ToList()));
        Assert.Null(BatteryRules.SlowCharger(Enumerable.Repeat(B(-12), 4).ToList()));                                     // fora da tomada
    }

    [Fact]
    public void Previsao_pelo_consumo_de_agora_sem_historico()
    {
        var f = BatteryRules.Forecast(30, 10, [])!;
        Assert.False(f.FromHistory);
        Assert.Equal(TimeSpan.FromMinutes(240), f.Light);     // 30 Wh / 7,5 W
        Assert.True(f.Light > f.Call && f.Call > f.Heavy);
        Assert.Null(BatteryRules.Forecast(30, 0, []));      // sem taxa nenhuma, não inventa
        Assert.Null(BatteryRules.Forecast(0, 10, []));
    }

    [Fact]
    public void Previsao_pelo_historico_da_pessoa()
    {
        var history = Enumerable.Range(1, 40).Select(i => (double)i).ToList();   // 1 a 40 W
        var f = BatteryRules.Forecast(40, 0, history)!;
        Assert.True(f.FromHistory);
        Assert.True(f.Light > f.Call && f.Call > f.Heavy);
        Assert.Equal(TimeSpan.FromMinutes(Math.Round(40 / 8.8 * 60)), f.Light);  // percentil 20 de 1..40 = 8,8 W
    }

    [Fact]
    public void Previsao_tem_teto_de_48_horas()
    {
        var f = BatteryRules.Forecast(500, 1, [])!;
        Assert.True(f.Light <= TimeSpan.FromHours(48));
    }

    [Fact]
    public void Duracao_legivel()
    {
        Assert.Equal(Loc.F("{0} min", 45), BatteryRules.Duration(TimeSpan.FromMinutes(45)));
        Assert.Equal(Loc.F("{0} h {1:00} min", 4, 10), BatteryRules.Duration(TimeSpan.FromMinutes(250)));
    }

    [Theory]
    [InlineData(92, 55, 90, 45)]
    [InlineData(88, 79.6, 70, 20)]
    public void Calor_que_freia_o_processador(double temp, double perf, double cpu, int expected) =>
        Assert.Equal(expected, BatteryRules.ThermalLossPct(temp, perf, cpu));

    [Theory]
    [InlineData(70, 50, 90)]   // frio: é limite de energia, não de calor
    [InlineData(92, 85, 90)]   // quente, mas rodando quase no máximo
    [InlineData(92, 50, 20)]   // quente, mas sem carga
    [InlineData(-1, 50, 90)]   // sem sensor
    public void Sem_estrangulamento_termico(double temp, double perf, double cpu) => Assert.Null(BatteryRules.ThermalLossPct(temp, perf, cpu));

    [Fact]
    public void Diagnostico_oferece_Modo_Colo_quando_o_calor_freia()
    {
        var hot = new Snapshot(Make.T0, 85, 50, 8, 16, 5, 40, 100, [], CpuPerf: 55, TempC: 93);
        var f = Diagnosis.Evaluate(Make.Minute(hot), Make.Health(), uptime: Make.FreshBoot);
        var lap = Assert.Single(f, x => x.FixId == "lap-mode");
        Assert.Contains("45", lap.TitleText);
        Assert.DoesNotContain(f, x => x.Title == "O processador está reduzindo a velocidade sob carga");   // um achado só, o mais preciso
    }

    [Fact]
    public void Diagnostico_oferece_Modo_sobrevivencia_na_bateria_abaixo_de_15()
    {
        var s = new Snapshot(Make.T0, 10, 40, 6, 16, 2, 40, 100, []);
        var low = Diagnosis.Evaluate(Make.Minute(s), Make.Health(), uptime: Make.FreshBoot, battery: [B(-9, pct: 12)]);
        Assert.Contains(low, x => x.FixId == "survival");
        Assert.DoesNotContain(Diagnosis.Evaluate(Make.Minute(s), Make.Health(), uptime: Make.FreshBoot, battery: [B(30, onAc: true, pct: 12, charging: true)]), x => x.FixId == "survival");
        Assert.DoesNotContain(Diagnosis.Evaluate(Make.Minute(s), Make.Health(), uptime: Make.FreshBoot, battery: [B(-9, pct: 40)]), x => x.FixId == "survival");
    }

    [Fact]
    public void Historico_da_bateria_guarda_so_descarga_dos_ultimos_7_dias_e_uma_capacidade_por_dia()
    {
        var h = new BatteryHistory();
        h.Add(B(-12) with { Time = Make.T0.AddDays(-8) });
        h.Add(B(-10) with { Time = Make.T0 });
        h.Add(B(40, onAc: true, charging: true) with { Time = Make.T0.AddMinutes(1) });
        h.Add(B(-11) with { Time = Make.T0.AddMinutes(2) });
        Assert.Equal(new[] { 10.0, 11.0 }, h.DischargeWatts());
        Assert.Equal(2, h.Capacity.Count);                     // o dia de 8 dias atrás e o de hoje
        var back = BatteryHistory.Parse(h.Serialize());
        Assert.Equal(h.DischargeWatts(), back.DischargeWatts());
        Assert.Empty(BatteryHistory.Parse("{quebrado").Discharge);
    }

    [Fact]
    public void Modo_sobrevivencia_guarda_como_estava()
    {
        var s = new SurvivalState("381b4222-f694-41f0-9685-ff5bb260df2e", 50, 20, 144, 1, 1, ["OneDrive", "Dropbox"]);
        Assert.Equal(s with { Eco = s.Eco }, SurvivalState.Parse(s.Serialize()) is { } p ? p with { Eco = s.Eco } : null);
        Assert.Equal(new[] { "OneDrive", "Dropbox" }, SurvivalState.Parse(s.Serialize())!.Eco);
        Assert.Null(SurvivalState.Parse(null));
        Assert.Null(SurvivalState.Parse("{x"));
    }

    [Fact]
    public void Tela_vai_para_60_Hz_ou_a_menor_que_houver()
    {
        Assert.Equal(60, SurvivalState.SaverRate(144, [60, 120, 144]));
        Assert.Equal(60, SurvivalState.SaverRate(165, [48, 60, 165]));   // 48 Hz é baixo demais para o uso normal
        Assert.Null(SurvivalState.SaverRate(60, [60, 120]));             // já está em 60
        Assert.Null(SurvivalState.SaverRate(75, [40, 75]));              // sem 60 Hz, não desce para uma taxa que cansa a vista
    }

    [Fact]
    public void Sobrevivencia_so_na_bateria_e_abaixo_de_15()
    {
        Assert.True(BatteryRules.OfferSurvival(B(-8, pct: 14)));
        Assert.False(BatteryRules.OfferSurvival(B(-8, pct: 15)));
        Assert.False(BatteryRules.OfferSurvival(B(20, onAc: true, pct: 5, charging: true)));
        Assert.False(BatteryRules.OfferSurvival(null));
    }

    [Fact]
    public void Demonstracao_tem_bateria_descarregando()
    {
        var b = DemoData.Battery(DateTime.Today, 10);
        Assert.True(b.Discharging && !b.OnAc && b.Percent > 0);
    }
}

/// <summary>Fase 11, leva 11.2: vampiros de bateria, sono, 60 Hz, USB e modo eficiência automático.</summary>
public class Fase11SonoTests
{
    private static HistSample H(DateTime t, bool? bat, params (string Name, double Cpu)[] p) =>
        new(t, 20, 40, 1, 50, [], P: p.ToDictionary(x => x.Name, x => new[] { x.Cpu, 100.0 }), B: bat);

    [Fact]
    public void Vampiros_de_bateria_contam_so_a_CPU_na_bateria()
    {
        var now = Make.T0;
        var samples = new[]
        {
            H(now.AddHours(-1), true, ("chrome", 30), ("Teams", 10)),
            H(now.AddHours(-2), true, ("chrome", 10), ("Teams", 30), ("Idle", 90)),
            H(now.AddHours(-3), false, ("Steam", 90)),          // na tomada: não conta
            H(now.AddHours(-4), null, ("Code", 90)),            // amostra antiga, sem a marca
            H(now.AddDays(-9), true, ("OneDrive", 90)),         // fora do período
        };
        var users = PowerAudit.BatteryUsers(samples, now.AddDays(-7));
        Assert.Equal(new[] { "chrome", "Teams" }, users.Select(u => u.Name).OrderBy(n => n));
        Assert.Equal(50, users[0].SharePct);
        Assert.Equal(2, users[0].Minutes);
        Assert.Empty(PowerAudit.BatteryUsers([H(now, false, ("x", 10))], now.AddDays(-1)));
    }

    [Fact]
    public void Pedidos_de_energia_do_powercfg()
    {
        const string output = """
            DISPLAY:
            [PROCESS] \Device\HarddiskVolume3\Program Files\Google\Chrome\Application\chrome.exe
            Video Wake Lock

            SYSTEM:
            [DRIVER] Realtek High Definition Audio (HDAUDIO\FUNC_01&VEN_10EC)
            An audio stream is currently in use.
            [SERVICE] \Device\HarddiskVolume3\Windows\System32\svchost.exe (wuauserv)

            AWAYMODE:
            Nenhum.

            EXECUTION:
            None.
            """;
        var r = PowerAudit.ParseRequests(output);
        Assert.Equal(3, r.Count);
        Assert.Equal(new PowerRequest("DISPLAY", "PROCESS", "chrome.exe"), r[0]);
        Assert.Equal("SYSTEM", r[1].Kind);
        Assert.Equal("DRIVER", r[1].Category);
        Assert.StartsWith("Realtek", r[1].Name);
        Assert.Equal("SERVICE", r[2].Category);
        Assert.Empty(PowerAudit.ParseRequests(""));
    }

    [Fact]
    public void Aparelhos_que_acordam_o_PC()
    {
        Assert.Equal(new[] { "HID Keyboard Device", "Intel(R) Wi-Fi 6 AX201" }, PowerAudit.ParseWakeDevices("HID Keyboard Device\r\nIntel(R) Wi-Fi 6 AX201\r\n\r\n"));
        Assert.Empty(PowerAudit.ParseWakeDevices("NONE\r\n"));
    }

    [Fact]
    public void Tela_em_60_Hz_na_bateria_e_de_volta_na_tomada()
    {
        Assert.Equal(60, PowerAudit.DynamicHzTarget(nowOnAc: false, 144, [60, 144], null));
        Assert.Equal(144, PowerAudit.DynamicHzTarget(nowOnAc: true, 60, [60, 144], 144));
        Assert.Null(PowerAudit.DynamicHzTarget(nowOnAc: true, 60, [60, 144], null));     // nada guardado: não inventa
        Assert.Null(PowerAudit.DynamicHzTarget(nowOnAc: false, 60, [60], null));         // tela de 60 Hz: nada a fazer
        Assert.Null(PowerAudit.DynamicHzTarget(nowOnAc: true, 60, [60, 120], 144));      // outra tela agora: a taxa guardada não existe
    }

    [Fact]
    public void Modo_eficiencia_fora_de_foco()
    {
        var (on, off) = PowerAudit.AutoEco(["chrome", "Teams", "Spotify"], ["chrome", "Teams", "explorer"], "chrome", ["chrome"]);
        Assert.Equal(new[] { "Teams" }, on);        // aberto e fora de foco; o Spotify não está aberto
        Assert.Equal(new[] { "chrome" }, off);      // voltou ao foco
        var again = PowerAudit.AutoEco(["chrome", "Teams"], ["chrome", "Teams"], "explorer", ["chrome", "Teams"]);
        Assert.Empty(again.TurnOn);
        Assert.Empty(again.TurnOff);
    }

    [Fact]
    public void Suspensao_seletiva_USB()
    {
        Assert.True(PowerAudit.UsbSuspendOff(0));
        Assert.False(PowerAudit.UsbSuspendOff(1));
        Assert.False(PowerAudit.UsbSuspendOff(null));
    }

    [Fact]
    public void Historico_grava_a_marca_de_bateria()
    {
        var s = H(Make.T0, true, ("chrome", 5));
        var back = System.Text.Json.JsonSerializer.Deserialize<HistSample>(System.Text.Json.JsonSerializer.Serialize(s))!;
        Assert.True(back.B);
        Assert.Null(System.Text.Json.JsonSerializer.Deserialize<HistSample>("""{"T":"2026-10-01T09:00:00","Cpu":1,"Ram":1,"Disk":1,"FreePct":1,"Top":[]}""")!.B);
    }
}

/// <summary>Na CI do Windows: as APIs de energia de verdade (o executor não tem bateria).</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class Fase11IntegrationTests
{
    [WindowsFact]
    public void Plano_de_energia_e_lido_pela_API_e_bateria_nao_falha_sem_bateria()
    {
        Assert.NotNull(PowerProbe.ActiveScheme());
        var boost = PowerProbe.ReadBoost();
        Assert.NotNull(boost);
        Assert.NotNull(PowerProbe.ReadSaverThreshold());
        PowerProbe.ReadBattery();          // no executor (sem bateria) volta null, sem exceção
        PowerProbe.RefreshRates();         // sem monitor de verdade pode não haver modos
        Assert.NotNull(PowerProbe.ReadUsbSuspend());
        PowerProbe.WakeDevices();          // sem administrador também funciona
        var (apps, system) = PowerProbe.ReadLightTheme();
        Assert.True(apps is null or 0 or 1 && system is null or 0 or 1);
    }
}
