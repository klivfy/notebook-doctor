using NotebookDoctor.Services;

namespace NotebookDoctor.Tests;

/// <summary>Fase 10, leva 10.1: destravador de arquivos e pendrives.</summary>
public class Fase10Tests
{
    private static LockingProcess P(int pid, string name, LockAppType type = LockAppType.MainWindow, string service = "") => new(pid, name, name, type, service);

    [Fact]
    public void Programa_comum_pode_ser_fechado()
    {
        Assert.Equal(LockVerdict.CanClose, FileLockRules.Classify(P(1234, "WINWORD"), 99));
        Assert.Equal(LockVerdict.CanClose, FileLockRules.Classify(P(1234, "notepad", LockAppType.Console), 99));
    }

    [Fact]
    public void Explorador_se_reinicia_e_nao_se_encerra()
    {
        Assert.Equal(LockVerdict.RestartExplorer, FileLockRules.Classify(P(500, "explorer", LockAppType.Explorer), 99));
        Assert.Equal(LockVerdict.RestartExplorer, FileLockRules.Classify(P(500, "Explorer", LockAppType.MainWindow), 99));
    }

    [Theory]
    [InlineData("lsass")]
    [InlineData("csrss")]
    [InlineData("MsMpEng")]
    [InlineData("svchost")]
    public void Processo_do_sistema_nunca_e_encerrado(string name) =>
        Assert.Equal(LockVerdict.Protected, FileLockRules.Classify(P(800, name), 99));

    [Fact]
    public void Critico_pid_do_sistema_e_servico_nao_sao_fechados()
    {
        Assert.Equal(LockVerdict.Protected, FileLockRules.Classify(P(900, "algum", LockAppType.Critical), 99));
        Assert.Equal(LockVerdict.Protected, FileLockRules.Classify(P(4, "System", LockAppType.Unknown), 99));
        Assert.Equal(LockVerdict.Service, FileLockRules.Classify(P(700, "SearchIndexer", LockAppType.Service, "WSearch"), 99));
        Assert.Equal(LockVerdict.Service, FileLockRules.Classify(P(700, "OneDriveSvc", LockAppType.Unknown, "OneDrive Updater"), 99));
    }

    [Fact]
    public void O_proprio_app_e_reconhecido()
    {
        Assert.Equal(LockVerdict.Self, FileLockRules.Classify(P(99, "NotebookDoctor"), 99));
        Assert.Equal(LockVerdict.Self, FileLockRules.Classify(P(99, "testhost"), 99));
    }

    [Fact]
    public void Lista_tira_repetidos_e_poe_primeiro_o_que_pode_fechar()
    {
        var order = FileLockRules.Order([P(10, "svchost"), P(20, "Zoom"), P(30, "explorer", LockAppType.Explorer), P(20, "Zoom"), P(40, "Acrobat")], 99);
        Assert.Equal(new[] { 40, 20, 30, 10 }, order.Select(p => p.Pid));
    }

    [Theory]
    [InlineData("WINWORD.EXE", "WINWORD")]
    [InlineData(@"C:\Program Files\App\app.exe", "app")]
    [InlineData("chrome", "chrome")]
    public void Nome_do_processo_sem_exe(string image, string expected) => Assert.Equal(expected, FileLockRules.ProcessName(image));

    [Theory]
    [InlineData("E:", true)]
    [InlineData(@"e:\", true)]
    [InlineData("E:/", true)]
    [InlineData(@"E:\Fotos", false)]
    [InlineData("EE:", false)]
    [InlineData("", false)]
    public void Raiz_de_unidade(string path, bool expected) => Assert.Equal(expected, FileLockRules.IsDriveRoot(path));

    [Fact]
    public void Tipo_do_alvo()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Docs\a.docx" };
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Docs", @"E:\" };
        LockTargetKind? Kind(string p) => FileLockRules.KindOf(p, files.Contains, dirs.Contains);
        Assert.Equal(LockTargetKind.File, Kind(@"C:\Docs\a.docx"));
        Assert.Equal(LockTargetKind.File, Kind("\"C:\\Docs\\a.docx\""));
        Assert.Equal(LockTargetKind.Folder, Kind(@"C:\Docs"));
        Assert.Equal(LockTargetKind.Drive, Kind("e:"));
        Assert.Null(Kind("F:"));                 // unidade que não existe
        Assert.Null(Kind(@"Docs\a.docx"));       // caminho relativo
        Assert.Null(Kind(@"C:\Nada"));
        Assert.Equal(@"E:\", FileLockRules.DriveRoot("e:"));
    }

    [Fact]
    public void Destravador_esta_no_smoke_nas_abas_e_na_bandeja()
    {
        Assert.Contains("new FileUnlockWindow(main)", File.ReadAllText(Path.Combine(RepoPaths.App, "Smoke.cs")));
        string main = File.ReadAllText(Path.Combine(RepoPaths.App, "MainWindow.xaml"));
        Assert.Equal(2, main.Split("Click=\"FileUnlock_Click\"").Length - 1);   // Limpeza e Espaço
        Assert.Contains("Click=\"SpaceWhoUses_Click\"", main);
        Assert.Contains("UnlockRequested", File.ReadAllText(Path.Combine(RepoPaths.App, "TrayIcon.cs")));
    }
}

/// <summary>Na CI do Windows: o Restart Manager de verdade.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class Fase10IntegrationTests
{
    [WindowsFact]
    public void Arquivo_aberto_por_este_processo_aparece_como_o_proprio_app()
    {
        string dir = Directory.CreateTempSubdirectory("nd-lock-").FullName;
        try
        {
            string file = Path.Combine(dir, "travado.txt");
            using (var fs = new FileStream(file, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                fs.WriteByte(1);
                var found = FileUnlocker.WhoIsUsing(FileUnlocker.FilesOf(file, LockTargetKind.File));
                var me = Assert.Single(found, p => p.Pid == Environment.ProcessId);
                Assert.Equal(LockVerdict.Self, FileLockRules.Classify(me, Environment.ProcessId));
                Assert.NotEqual(0, me.StartFileTime);
                // A pasta também acha quem segura um arquivo de dentro dela
                Assert.Contains(FileUnlocker.WhoIsUsing(FileUnlocker.FilesOf(dir, LockTargetKind.Folder)), p => p.Pid == Environment.ProcessId);
                // E o app nunca fecha a si mesmo
                Assert.Equal(CloseResult.NotAllowed, FileUnlocker.Close(me));
            }
            Assert.DoesNotContain(FileUnlocker.WhoIsUsing([file]), p => p.Pid == Environment.ProcessId);
        }
        finally { Directory.Delete(dir, true); }
    }

    [WindowsFact]
    public void Disco_do_sistema_nao_e_ejetado_e_lista_de_pendrives_nao_falha()
    {
        Assert.Equal(EjectResult.NotRemovable, FileUnlocker.Eject(Path.GetPathRoot(Environment.SystemDirectory)![0]));
        Assert.All(FileUnlocker.RemovableDrives(), d => Assert.Equal(DriveType.Removable, d.DriveType));
    }
}
