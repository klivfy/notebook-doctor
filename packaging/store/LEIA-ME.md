# Guia de Publicação na Microsoft Store (MSIX)

Este documento orienta o processo de homologação e envio do **Notebook Doctor** para a **Microsoft Store** via **Microsoft Partner Center**.

---

## 1. Como gerar os pacotes para a Loja

O script `scripts/release/empacotar-msix.ps1` compila o aplicativo com o símbolo `STORE` (`-p:StoreBuild=true`) e gera os arquivos `.msix` prontos para upload no Partner Center.

```powershell
# Gerar pacotes para x64 e Arm64
pwsh scripts/release/empacotar-msix.ps1 -PublisherId "CN=SEU-PUBLISHER-ID-DO-PARTNER-CENTER" -PackageIdentityName "KlivfyTecnologia.NotebookDoctor" -Saida "msix-out"
```

Os pacotes gerados em `msix-out/` serão:
- `NotebookDoctor-x64.msix`
- `NotebookDoctor-arm64.msix`

> **Nota:** Não é necessário assinar digitalmente com certificado próprio para a Microsoft Store. O Partner Center ingere os arquivos `.msix` e assina os pacotes automaticamente com o certificado oficial da Microsoft durante a ingestão.

---

## 2. Diferenças da Edição da Microsoft Store (`STORE`)

Para cumprir estritamente as políticas de segurança e distribuição da Microsoft Store (Seção 10.2):

| Recurso | Distribuição Direta (Inno Setup) | Microsoft Store (MSIX) | Motivo / Política |
| --- | --- | --- | --- |
| **Atualizador Próprio** | Ativo (`Updater.cs`, download de `klivfy.com`) | **Desativado** | Política 10.2.5: atualizações são gerenciadas exclusivamente pela Microsoft Store. |
| **Atualizador WinGet** | Ativo (aba Programas › Atualizar programas) | **Desativado** | Política 10.2.3: proibição de instalação de softwares secundários de terceiros. |
| **Drivers pelo Windows Update** | Ativo (Configurações › Drivers) | **Desativado** | Política 10.2.4: restrição para manipulação de drivers de terceiros. |
| **Drivers / App do Fabricante** | Ativo (links informativos OEM) | **Ativo** | Apenas orienta o usuário para o aplicativo do fabricante do notebook. |
| **Diagnóstico e Limpeza** | Ativo (CPU, RAM, Disco, Bateria, Lixo) | **Ativo** | Recursos centrais do produto. |
| **Privacidade** | Sem telemetria por padrão | **Sem telemetria por padrão** | Política de privacidade pública em `https://klivfy.com/privacidade`. |

---

## 3. Justificativa da Capacidade Restrita (`runFullTrust`)

Ao cadastrar a submissão no Partner Center, o sistema exigirá uma declaração e justificativa para o uso da capacidade restrita `runFullTrust`. Copie e utilize o texto abaixo nas **Notas de Certificação** (em inglês e português):

### Texto para as Notas de Certificação (Submission / Certification Notes):

> **Capacidade:** `runFullTrust`
>
> **English Justification:**
> *"Notebook Doctor is a native desktop system utility and diagnostic tool designed for Windows notebooks. It requires the `runFullTrust` desktop bridge capability to inspect local hardware performance counters (CPU throttling, memory usage, disk health via SMART APIs, battery wear and charge cycles), perform user-approved cleanups of standard Windows temporary directories (AppData\Local\Temp), inspect running processes for high-resource alerts, and allow safe, user-confirmed system maintenance. All actions are strictly initiated by the user or run in read-only simulation mode by default. The Microsoft Store edition disables standalone self-updaters and third-party software installers in full compliance with Store Policy 10.2."*
>
> **Justificativa em Português:**
> *"O Notebook Doctor é um utilitário nativo de diagnóstico e manutenção para notebooks com Windows. Ele necessita da capacidade `runFullTrust` para ler contadores de desempenho de hardware (temperatura/throttling de CPU, uso de RAM, desgaste de bateria e ciclos de recarga, saúde do disco via SMART), realizar limpeza de arquivos temporários autorizada pelo usuário, listar processos pesados em segundo plano e permitir manutenção segura. A edição da Loja não realiza downloads nem instalações de terceiros e não possui atualizador autônomo, respeitando integralmente as Políticas da Store."*

---

## 4. Checklist de Submissão no Partner Center

1. **Identidade do Pacote:**
   - Reserve o nome `Notebook Doctor`.
   - Copie o **Nome do pacote** e o **Publisher ID** da página *Gerenciamento de Pacotes / Identidade* e utilize como parâmetros no `empacotar-msix.ps1`.
2. **Propriedades:**
   - Categoria: *Utilitários e Ferramentas* (Utilities & Tools).
   - Classificação etária: Livre / Todas as idades (IARC).
3. **Listagem na Loja por Idioma (Store Listings):**
   - **Português (Brasil):**
     - Política de Privacidade: `https://klivfy.com/pt-BR/privacidade`
     - Termos de Uso: `https://klivfy.com/pt-BR/termos-de-uso`
     - Suporte: `https://klivfy.com/suporte`
   - **English (United States):**
     - Privacy Policy: `https://klivfy.com/en-US/privacidade`
     - Terms of Use: `https://klivfy.com/en-US/termos-de-uso`
     - Support: `https://klivfy.com/suporte`
   - **Español (España / Latam):**
     - Política de Privacidad: `https://klivfy.com/es-ES/privacidade`
     - Términos de Uso: `https://klivfy.com/es-ES/termos-de-uso`
     - Soporte: `https://klivfy.com/suporte`
   - Capturas de tela e kit completo da vitrine: consulte `packaging/store/vitrine/` (`listagem-pt-BR.md`, `listagem-en-US.md`, `listagem-es-ES.md`, `questionario-iarc-e-certificacao.md`, `artes/` e `screenshots/{pt-BR,en-US,es-ES}/`). Para regerar as capturas em `1920×1080`, execute `NotebookDoctor.exe --demo --store-screenshots=<pasta>`.
4. **Pacotes:**
   - Faça upload de `NotebookDoctor-x64.msix` e `NotebookDoctor-arm64.msix`.

