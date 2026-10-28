// Tudo aqui usa o registro, o Agendador de Tarefas e a Lixeira do Windows
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]
// DLLs do sistema só da pasta System32: uma DLL com o mesmo nome deixada ao lado do executável (pasta Downloads, pendrive) não é carregada
[assembly: System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
