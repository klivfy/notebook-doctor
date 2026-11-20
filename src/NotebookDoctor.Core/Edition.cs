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

    /// <summary>
    /// Apagar entradas do registro (Limpeza › Verificar registro). Fora da Store, só com prova de que a entrada está quebrada,
    /// com backup e Desfazer. Fica de fora da primeira edição da Store: a Microsoft não dá suporte a limpadores de registro
    /// (KB 2563254) e a certificação avalia isso; pode voltar depois, com a certificação aprovada e uma justificativa por escrito.
    /// </summary>
    public static bool RegistryCleanup => !IsStore;

    /// <summary>
    /// Importar e usar regras de limpeza de um arquivo winapp2.ini escolhido pela pessoa (Limpeza avançada › Rastros e programas).
    /// Na Store ficam só as regras que vão no app: um arquivo externo poderia apontar para qualquer pasta.
    /// </summary>
    public static bool CommunityRules => !IsStore;
}
