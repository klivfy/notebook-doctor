# Release assinada (A50 / S2)

O app só instala uma atualização se **as três** conferências passarem (`Services/Updater.cs`):

1. o SHA-256 do arquivo baixado é o do `versao.json`;
2. a assinatura Authenticode é válida (WinVerifyTrust);
3. o certificado tem `O` e `CN` exatamente `Klivfy` e, com `PublisherSignature.PinnedKeys` preenchida, a chave pública dele está na lista.

O manifesto vem de `https://klivfy.com/notebookdoctor/versao.json`, e o endereço do instalador precisa ser `https` em `klivfy.com`. A leitura do manifesto está em `src/NotebookDoctor.Core/UpdateManifest.cs`, com testes em `UpdateSecurityTests`.

## O que a Klivfy precisa ter

- Um certificado de assinatura de código em nome de **Klivfy** (O e CN iguais a `Klivfy`), no repositório de certificados do Windows ou em token/HSM.
- Um **instalador** (`NotebookDoctor-Setup-<versão>.exe`). O repositório ainda não tem um: o app baixa o arquivo, confere e abre, então ele precisa instalar o app, e não ser só o executável do app.
- Espaço no site para `.../notebookdoctor/downloads/` e para `.../notebookdoctor/versao.json`.

## Passos

1. Com o certificado público (`.cer`, sem a chave privada): `pwsh scripts/release/chave-publica.ps1 -Arquivo certificado.cer`. Copie a linha `PinnedKeys` para `PublisherSignature.PinnedKeys` e faça o commit (liste mais de uma chave quando houver um certificado de reserva, para trocar de certificado sem quebrar a atualização).
2. `pwsh scripts/release/gerar-release.ps1 -Versao 1.2.0 -Instalador <caminho> -Impressao <SHA-1 do certificado>`: publica x64 e arm64, assina (SHA-256 e carimbo de tempo RFC 3161), confere, calcula o hash **depois** de assinar e escreve o `versao.json`.
3. Publique o instalador e o `versao.json`. Só publique o `versao.json` depois do instalador.
4. Numa máquina de teste com a versão anterior: Configurações › Procurar atualização › Instalar.

Os scripts não foram executados no ambiente de desenvolvimento (Linux, sem signtool nem o certificado).
