namespace NotebookDoctor.Services;

/// <summary>
/// Edição do app. A da Microsoft Store (compilada com <c>-p:StoreBuild=true</c>, que define <c>STORE</c>) só é
/// atualizada pela própria Store e não instala nem atualiza software ou drivers de terceiros: o atualizador próprio,
/// o atualizador de programas pelo WinGet e os drivers ficam desligados. Fora da Store nada muda.
/// </summary>
public static class Edition
{
#if STORE
    public static bool IsStore => true;
#else
    public static bool IsStore => false;
#endif

    /// <summary>Procurar e instalar versão nova do próprio app pelo site da Klivfy. Na Store, quem atualiza é a Store.</summary>
    public static bool SelfUpdate => !IsStore;

    /// <summary>Atualizar programas pelo WinGet e instalar drivers (Windows Update ou fabricante).</summary>
    public static bool ThirdPartyInstalls => !IsStore;
}
