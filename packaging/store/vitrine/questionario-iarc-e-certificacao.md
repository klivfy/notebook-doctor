# Guia de Preenchimento — Questionário IARC, Propriedades e Notas de Certificação (Partner Center)

Este documento reúne todas as respostas exatas para as seções **Propriedades (Properties)**, **Classificações Etárias (Age ratings / IARC)**, **Requisitos de Sistema** e **Notas para Certificação (Notes for certification)** no Microsoft Partner Center.

---

## 1. Notas para Certificação (*Notes for certification* — Obrigatório para MSIX `runFullTrust`)

Copie e cole o texto abaixo (em inglês e português, pois a equipe de revisão da Microsoft Store avalia submissões globais em inglês) no campo **Notes for certification** da página **Submission options**:

```text
[ENGLISH — Reviewer Notes for runFullTrust Desktop Bridge App]
Notebook Doctor is a native Windows desktop system diagnostics, hardware monitoring, disk cleanup, and power management utility (.NET 8 WPF packaged via MSIX Desktop Bridge).

1. Justification for "runFullTrust" restricted capability:
   - Reads Windows hardware & performance telemetry via WMI/CIM, Performance Counters, S.M.A.R.T. disk health, battery discharge rate (Watts), and Windows Event Logs.
   - Manages user startup entries (HKCU\...\StartupApproved), Windows power plans (powercfg), process priorities (EcoQoS / Efficiency Mode), and user temporary files.
   - All system modifications are user-initiated, reversible via the built-in "Actions" (Undo) tab, and can be previewed safely without touching the OS using the built-in "Simulation Mode".

2. Microsoft Store Policy 10.2.4 Compliance (Store Edition Build):
   - This MSIX package was compiled with "-p:StoreBuild=true" (Edition.IsStore == true).
   - The self-updater (GitHub Releases updater) and third-party software/driver downloaders (WinGet / Windows Update COM installer) are completely disabled at compile time and hidden from the UI. Updates are delivered exclusively through the Microsoft Store.

3. Testing Instructions for Reviewer:
   - No login or external account is required. The app works 100% locally and offline.
   - Standard user permissions are sufficient to launch and use all core tabs (Home/Dashboard, Processes, Cleanup, Disk Space, Startup, History, Actions, Tweaks, System, and Settings).
   - You can toggle "Simulation Mode" in the "Tweaks" tab (or click the top Simulation chip) to test any optimization safely without modifying the test machine.

---

[PORTUGUÊS — Notas para o Revisor]
O Notebook Doctor é um utilitário desktop nativo (.NET 8 WPF empacotado em MSIX) para diagnóstico de saúde do PC, limpeza de temporários e eficiência de bateria.
- Permissão runFullTrust: necessária para leitura de contadores de hardware/WMI, S.M.A.R.T. do disco, taxa de descarga da bateria, gerenciamento de inicialização (StartupApproved) e planos de energia.
- Conformidade com a Política 10.2.4 da Store: compilado com StoreBuild=true; o auto-atualizador externo e os instaladores de pacotes/drivers de terceiros estão desativados nesta edição da Microsoft Store.
- Nenhum login ou conta é necessário para testar o aplicativo.
```

---

## 2. Questionário de Classificação Etária (IARC — *Age Ratings*)

No Partner Center, acesse **Age ratings**:

1. **Tipo de aplicativo (Category):** Selecione **`Utility, Productivity, Communication, or Other`** (Utilitário, Produtividade, Comunicação ou Outro). **Não** selecione "Game".
2. **Respostas para todas as perguntas do questionário IARC:**
   - **Violence (Violência):** `No` (Não)
   - **Fear (Medo):** `No` (Não)
   - **Sexuality / Nudity (Sexualidade / Nudez):** `No` (Não)
   - **Simulated Gambling (Jogos de azar simulados):** `No` (Não)
   - **Language / Profanity (Linguagem imprópria):** `No` (Não)
   - **Controlled Substances — Drugs, Alcohol, Tobacco (Substâncias controladas):** `No` (Não)
   - **Crude Humor (Humor grosseiro):** `No` (Não)
   - **Users Interact / Shares Info / Shares Location / Unrestricted Internet:**
     - *Does the app allow users to interact or exchange content with other users?* → **`No`**
     - *Does the app share the user's current physical location with other users?* → **`No`**
     - *Does the app allow purchases of digital goods (in-app purchases)?* → **`No`**
     - *Does the app provide unrestricted web browsing?* → **`No`**

> **Resultado esperado da classificação:** **Livre para todos os públicos** (`ClassInd: L` no Brasil, `ESRB: Everyone` nos EUA, `PEGI: 3` na Europa).

---

## 3. Propriedades e Declarações Legais (*Properties*)

| Campo no Partner Center | Valor Recomendado |
|---|---|
| **Category** | `Utilities & tools` (Utilitários e ferramentas) |
| **Display mode** | PC (`Windows.Desktop`) |
| **System requirements — Touch / Keyboard / Mouse** | Marcar **Keyboard** (`Recommended`) e **Integrated mouse** (`Recommended`). Touch é opcional. |
| **Minimum hardware** | `OS: Windows 10 version 19041.0 or higher` · `Architecture: x64` · `RAM: 4 GB minimum (8 GB recommended)` · `Storage: 200 MB available space` |
| **Declarations — Can your app access, collect, or transmit personal information?** | Selecione **`No`** (se mantiver a telemetria anônima desligada por padrão, pois nenhum dado pessoal ou identificador do usuário sai do computador) **ou** **`Yes`** informando a URL da Política de Privacidade nos 3 idiomas (recomendado preencher sempre a URL da Política de Privacidade em *Store listings*, independentemente dessa caixa). |

---

## 4. Tabela de URLs por Idioma para Colar em *Store Listings*

| Idioma no Partner Center | Privacy Policy URL | License Terms (Terms of Use) URL | Support Contact / Website |
|---|---|---|---|
| **Portuguese (Brazil)** (`pt-BR`) | `https://klivfy.com/pt-BR/privacidade` | `https://klivfy.com/pt-BR/termos-de-uso` | `https://klivfy.com/suporte` |
| **English (United States)** (`en-US`) | `https://klivfy.com/en-US/privacidade` | `https://klivfy.com/en-US/termos-de-uso` | `https://klivfy.com/suporte` |
| **Spanish** (`es-ES` / `es`) | `https://klivfy.com/es-ES/privacidade` | `https://klivfy.com/es-ES/termos-de-uso` | `https://klivfy.com/suporte` |

