; Instalador do Notebook Doctor (Inno Setup 6.3 ou mais novo).
; Não se compila à mão: use scripts/release/construir-instalador.ps1, que passa Versao, OrigemX64, OrigemArm64 e Saida.
;
; Decisões:
;  - Instala só para a pessoa (sem administrador), em %LocalAppData%\Programs, como o app já assume (asInvoker).
;  - Um instalador só para x64 e Arm64: copia o executável da arquitetura do Windows.
;  - O atalho do Menu Iniciar tem o nome que o app espera (Notebook Doctor.lnk, B21), direto na pasta Programas.
;  - Não liga "iniciar com o Windows" nem estatísticas: isso a pessoa escolhe dentro do app.
;  - O desinstalador não apaga os dados da pessoa (%LocalAppData%\NotebookDoctor: configurações, backups, Ações).

#ifndef Versao
  #error Passe /DVersao=1.2.0
#endif
#ifndef OrigemX64
  #error Passe /DOrigemX64=<pasta do publish win-x64>
#endif
#ifndef OrigemArm64
  #error Passe /DOrigemArm64=<pasta do publish win-arm64>
#endif
#ifndef Saida
  #define Saida "."
#endif

#define Nome "Notebook Doctor"
#define Exe "NotebookDoctor.exe"
#define Empresa "Klivfy Tecnologia e Consultoria em TI LTDA"

[Setup]
AppId={{E16D6632-EE2D-466E-948C-EB302B091029}
AppName={#Nome}
AppVersion={#Versao}
AppVerName={#Nome} {#Versao}
AppPublisher={#Empresa}
AppPublisherURL=https://klivfy.com
AppSupportURL=https://klivfy.com/notebookdoctor
AppCopyright=© 2026 {#Empresa}. Todos os direitos reservados.
VersionInfoVersion={#Versao}
VersionInfoCompany={#Empresa}
VersionInfoDescription=Instalador do {#Nome}
DefaultDirName={autopf}\{#Nome}
UsePreviousAppDir=yes
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
DisableDirPage=auto
DisableReadyPage=yes
ArchitecturesAllowed=x64compatible or arm64
ArchitecturesInstallIn64BitMode=x64compatible or arm64
MinVersion=10.0.17763
SetupIconFile=..\src\NotebookDoctor\app.ico
UninstallDisplayIcon={app}\{#Exe}
UninstallDisplayName={#Nome}
OutputDir={#Saida}
OutputBaseFilename=NotebookDoctor-Setup-{#Versao}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Atualização feita pelo próprio app: fecha o Notebook Doctor aberto e o abre de novo no fim
CloseApplications=yes
RestartApplications=yes

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#OrigemX64}\{#Exe}"; DestDir: "{app}"; Flags: ignoreversion; Check: not IsArm64
Source: "{#OrigemArm64}\{#Exe}"; DestDir: "{app}"; Flags: ignoreversion; Check: IsArm64

[Icons]
Name: "{autoprograms}\{#Nome}"; Filename: "{app}\{#Exe}"; Comment: "Monitor de desempenho, limpeza segura e diagnóstico"
Name: "{autodesktop}\{#Nome}"; Filename: "{app}\{#Exe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#Exe}"; Description: "{cm:LaunchProgram,{#Nome}}"; Flags: nowait postinstall skipifsilent
