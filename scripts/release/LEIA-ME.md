# Release assinada (A50 / S2)

O app só instala uma atualização se **as três** conferências passarem (`Services/Updater.cs`):

1. o SHA-256 do arquivo baixado é o do `versao.json`;
2. a assinatura Authenticode é válida (WinVerifyTrust);
3. o certificado tem `O` e `CN` exatamente `Klivfy` e, com `PublisherSignature.PinnedKeys` preenchida, a chave pública dele está na lista.

O manifesto vem de `https://klivfy.com/notebookdoctor/versao.json`, e o endereço do instalador precisa ser `https` em `klivfy.com`. A leitura do manifesto está em `src/NotebookDoctor.Core/UpdateManifest.cs`, com testes em `UpdateSecurityTests`.

## O que a Klivfy precisa ter

- Um certificado de assinatura de código em nome de **Klivfy** (O e CN iguais a `Klivfy`), no repositório de certificados do Windows ou em token/HSM.
- Um **instalador** (`NotebookDoctor-Setup-<versão>.exe`): o app baixa o arquivo, confere e abre, então ele precisa instalar o app. O script está em `installer/NotebookDoctor.iss` (Inno Setup 6.3+): instala só para a pessoa, sem administrador, em `%LocalAppData%\Programs\Notebook Doctor`; leva o executável x64 ou Arm64 conforme o Windows; cria o atalho `Notebook Doctor.lnk` no Menu Iniciar (o nome que o app espera, B21) e, se a pessoa quiser, na Área de Trabalho; fecha o app aberto numa atualização e o abre de novo; entra em Aplicativos instalados. Não liga o início com o Windows nem apaga os dados da pessoa ao desinstalar. Idiomas do assistente: português, inglês e espanhol.
- **Inno Setup 6.3 ou mais novo** na máquina que gera a release (`choco install innosetup`).
- Espaço no site para `.../notebookdoctor/downloads/` e para `.../notebookdoctor/versao.json`.

## Passos

1. Com o certificado público (`.cer`, sem a chave privada): `pwsh scripts/release/chave-publica.ps1 -Arquivo certificado.cer`. Copie a linha `PinnedKeys` para `PublisherSignature.PinnedKeys` e faça o commit (liste mais de uma chave quando houver um certificado de reserva, para trocar de certificado sem quebrar a atualização).
2. `pwsh scripts/release/gerar-release.ps1 -Versao 1.2.0 -Impressao <SHA-1 do certificado>`: publica x64 e arm64, assina os executáveis, constrói o instalador com eles (`construir-instalador.ps1`), assina o instalador (SHA-256 e carimbo de tempo RFC 3161), confere, calcula o hash **depois** de assinar e escreve o `versao.json`. Com `-Instalador <caminho>` usa um instalador já pronto em vez de construir.
3. Publique o instalador e o `versao.json`. Só publique o `versao.json` depois do instalador.
4. Numa máquina de teste com a versão anterior: Configurações › Procurar atualização › Instalar.

## Instalador sem assinatura (testes)

A CI (job `instalador`) constrói o instalador, instala e desinstala sem janelas e confere atalho e registro de desinstalação. O arquivo vem no artefato **instalador**, sem assinatura: o Windows mostra o SmartScreen e o app **recusa** instalá-lo como atualização. Serve para testar a instalação à mão, nunca para distribuir. O mesmo se faz localmente com `pwsh scripts/release/construir-instalador.ps1 -Versao 1.2.0 -OrigemX64 <pasta> -OrigemArm64 <pasta>`.

Os scripts de assinatura (`gerar-release.ps1`) não foram executados no ambiente de desenvolvimento (Linux, sem signtool nem o certificado). O instalador em si é construído e testado na CI do Windows.
