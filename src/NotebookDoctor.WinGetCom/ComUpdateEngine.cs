using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Management.Deployment;
using WinRT;

namespace NotebookDoctor.Services;

/// <summary>
/// Prioridade 5 (D2/C14): consulta o catálogo do WinGet pela API COM oficial (Microsoft.Management.Deployment), em vez de
/// ler a tabela de texto do terminal. Só lê: lista o que tem versão nova e monta o texto de dados e termos do pacote.
/// Quem instala é a linha de comando, depois do aceite da pessoa. Qualquer falha de ativação sobe como exceção para o
/// <see cref="FailoverUpdateEngine"/> passar para a reserva.
/// </summary>
public sealed class ComUpdateEngine : IUpdateEngine
{
    // CLSIDs de lançamento do servidor COM do Instalador de Aplicativo (ComClsids.h do winget-cli, versão de produção)
    private static readonly Guid ClsidPackageManager = new("C53A4F16-787E-42A4-B304-29EFFB4BF597");
    private static readonly Guid ClsidFindPackagesOptions = new("572DED96-9C60-4526-8F92-EE7D91D38C1A");
    private static readonly Guid ClsidCreateCompositePackageCatalogOptions = new("526534B8-7E46-47C8-8416-B1685C327D37");

    public string Name => "COM";

    private static T Activate<T>(Guid clsid)
    {
        try
        {
            var type = Type.GetTypeFromCLSID(clsid) ?? throw new UpdateEngineUnavailableException("O servidor COM do WinGet não está registrado.");
            object instance = Activator.CreateInstance(type) ?? throw new UpdateEngineUnavailableException("O WinGet não criou o objeto COM.");
            IntPtr unknown = Marshal.GetIUnknownForObject(instance);
            try { return MarshalInterface<T>.FromAbi(unknown); }
            finally { Marshal.Release(unknown); }
        }
        catch (COMException ex) { throw new UpdateEngineUnavailableException("Falha ao ativar o WinGet por COM.", ex); }
    }

    private static PackageManager Manager() => Activate<PackageManager>(ClsidPackageManager);

    private static async Task<PackageCatalog> ConnectAsync(PackageManager manager, bool sourceTermsAccepted, CancellationToken ct)
    {
        var remote = manager.GetPackageCatalogByName(WingetCommands.Source)
            ?? throw new UpdateEngineUnavailableException("O catálogo do WinGet não está configurado.");
        remote.AcceptSourceAgreements = sourceTermsAccepted; // só vale o que a pessoa aceitou na tela

        var options = Activate<CreateCompositePackageCatalogOptions>(ClsidCreateCompositePackageCatalogOptions);
        options.Catalogs.Add(remote);
        options.CompositeSearchBehavior = CompositeSearchBehavior.LocalCatalogs; // o que está instalado, com a versão nova do catálogo
        var connect = await manager.CreateCompositePackageCatalog(options).ConnectAsync().AsTask(ct);
        if (connect.Status != ConnectResultStatus.Ok)
            throw new UpdateEngineUnavailableException($"O WinGet não conectou ao catálogo ({connect.Status}).");
        return connect.PackageCatalog;
    }

    public async Task<UpdateScan> ListUpgradesAsync(bool sourceTermsAccepted, CancellationToken ct)
    {
        var catalog = await ConnectAsync(Manager(), sourceTermsAccepted, ct);
        var find = await catalog.FindPackagesAsync(Activate<FindPackagesOptions>(ClsidFindPackagesOptions)).AsTask(ct);
        if (find.Status != FindPackagesResultStatus.Ok)
            throw new UpdateEngineUnavailableException($"A busca do WinGet falhou ({find.Status}).");

        var items = new List<UpdateCandidate>();
        foreach (var match in find.Matches)
        {
            var package = match.CatalogPackage;
            if (!package.IsUpdateAvailable) continue;
            string id = package.Id;
            if (!ProgramUpdateRules.IdOk(id)) continue; // ARP\… e MSIX\…: fora do catálogo, como na reserva
            var available = package.DefaultInstallVersion;
            string installed = package.InstalledVersion?.Version ?? "";
            string newVersion = available?.Version ?? "";
            if (newVersion.Length == 0 || !VersionOrder.IsNewer(newVersion, installed)) continue;
            items.Add(new UpdateCandidate(package.Name, id, installed, newVersion, available?.PackageCatalog?.Info?.Name ?? WingetCommands.Source));
        }
        return new UpdateScan(new WingetList(items, 0), Name);
    }

    public async Task<UpdateDetails?> ShowAsync(string id, bool sourceTermsAccepted, CancellationToken ct)
    {
        if (!ProgramUpdateRules.IdOk(id)) throw new ArgumentException("Identificador de pacote inválido.", nameof(id));
        var catalog = await ConnectAsync(Manager(), sourceTermsAccepted, ct);
        var find = await catalog.FindPackagesAsync(Activate<FindPackagesOptions>(ClsidFindPackagesOptions)).AsTask(ct);
        if (find.Status != FindPackagesResultStatus.Ok)
            throw new UpdateEngineUnavailableException($"A busca do WinGet falhou ({find.Status}).");

        var package = find.Matches.Select(m => m.CatalogPackage).FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        var version = package?.DefaultInstallVersion;
        if (package is null || version is null) return null;
        return new UpdateDetails(Describe(package, version), Name);
    }

    /// <summary>Dados oficiais do pacote e, ao final, os termos exatamente como o catálogo os traz: é o que a pessoa lê antes de aceitar.</summary>
    private static string Describe(CatalogPackage package, PackageVersionInfo version)
    {
        var meta = version.GetCatalogPackageMetadata();
        var text = new StringBuilder();
        void Line(string label, string? value) { if (!string.IsNullOrWhiteSpace(value)) text.Append(label).Append(": ").AppendLine(value.Trim()); }
        Line("Name", package.Name);
        Line("Id", package.Id);
        Line("Version", version.Version);
        Line("Publisher", meta.Publisher);
        Line("Author", meta.Author);
        Line("Description", string.IsNullOrWhiteSpace(meta.Description) ? meta.ShortDescription : meta.Description);
        Line("License", meta.License);
        Line("License Url", meta.LicenseUrl);
        Line("Privacy Url", meta.PrivacyUrl);
        Line("Copyright", meta.Copyright);
        Line("Release Notes Url", meta.ReleaseNotesUrl);
        foreach (var agreement in meta.Agreements)
        {
            text.AppendLine().Append(agreement.Label).AppendLine(":");
            if (!string.IsNullOrWhiteSpace(agreement.Text)) text.AppendLine(agreement.Text.Trim());
            if (!string.IsNullOrWhiteSpace(agreement.Url)) text.AppendLine(agreement.Url.Trim());
        }
        return text.ToString().Trim();
    }
}
