# Estudo de viabilidade e plano de ação: desinstalador completo, limpeza avançada e ferramentas do sistema

**Objetivo:** levar ao Notebook Doctor as funções do Revo Uninstaller Pro e do CCleaner Professional Plus.
**Data do estudo:** 22/10/2026 · **Versões de referência:** Revo Uninstaller Pro 5.5.4 e CCleaner 7.x Professional / Professional Plus.

---

## 1. Resumo

| Pergunta | Resposta |
|---|---|
| Dá para fazer engenharia reversa dos dois programas? | **Não, e não é preciso.** As licenças dos dois proíbem descompilar e desmontar (seção 2). Copiar código, textos, ícones ou bases de dados deles seria infração de direito autoral. |
| Dá para ter as mesmas funções? | **Sim, quase todas.** Função não tem proteção de direito autoral; o código que a implementa tem. Cada função dos dois produtos usa APIs públicas e documentadas do Windows, então dá para escrever tudo do zero (método "sala limpa", seção 3). |
| Quantas funções são viáveis? | De **52 funções** distintas levantadas: **5 já existem** no Notebook Doctor, **30 são viáveis**, **13 são viáveis com limites** e **4 não serão feitas** (seção 4.6). |
| Esforço estimado | **17 levas planejadas** (cerca de 20 com folga para as maiores), do mesmo tamanho das levas das Fases 1 a 4, em 4 fases novas (5 a 8). Um motor próprio de recuperação de arquivos somaria mais 3 a 4. |
| O que depende de você | 9 decisões (seção 7), entre elas: aceitar o método sala limpa, liberar 1 dependência NuGet da Microsoft, o escopo do "limpador de registro" e o caminho da recuperação de arquivos. |

**A maior entrega é o desinstalador completo (Fase 5).** Ele é a base do Revo e agora também do CCleaner 7, que ganhou um motor de desinstalação novo. É também onde está o maior risco: apagar algo que não era do programa. Por isso o plano começa por ele, com três níveis de confiança, backup de tudo e testes automatizados num Windows real (CI).

---

## 2. Por que não fazer engenharia reversa dos binários

### 2.1 O que as licenças dizem

- **Revo Uninstaller (VS Revo Group),** "LICENSE AGREEMENT AND COPYRIGHT", em "You are NOT allowed to":
  > "1. Modify, reverse engineer, decompile, disassemble or otherwise attempt to reconstruct or discover the source code, or any parts of it from the binaries of Revo Uninstaller."

  Fonte: cópia da licença da versão gratuita em revo-uninstaller.soft112.com. A licença da versão Pro não está publicada no site do fabricante; vale conferir na tela de instalação.
- **CCleaner (Piriform, hoje Gen Digital),** licença ao consumidor, seção 3:
  > "You may NOT decompile, disassemble, reverse engineer or otherwise attempt to discover the source code of the Product except to the extent that you may be expressly permitted to reverse engineer or decompile under applicable law."

  O contrato atual da Gen Digital (ccleaner.com/legal) mantém a proibição, com a mesma exceção do que a lei permitir. A Política de Uso Aceitável também restringe testar e publicar comparativos de desempenho sem autorização por escrito.

### 2.2 O que a lei brasileira permite

> Não é parecer jurídico. Antes de divulgar comparações com os concorrentes, vale uma revisão de um advogado.

- **Lei 9.610/98, art. 8º, I:** ideias, procedimentos, sistemas e métodos não são protegidos por direito autoral. A função "desinstalar e procurar sobras" pode ser reproduzida; o código do Revo, não.
- **Lei 9.609/98 (Lei do Software), art. 6º, III:** não ofende os direitos do autor a semelhança de um programa com outro que já existia, quando ela vem das características funcionais da aplicação, de normas técnicas ou da falta de outra forma de expressão.
- **Lei 9.279/96 (marcas):** "Revo Uninstaller", "CCleaner", "Recuva", "Speccy" e "Defraggler" são marcas registradas (a da Gen Digital está declarada em ccleaner.com/legal). **Não usar esses nomes** na interface, no instalador nem na divulgação do Notebook Doctor.

### 2.3 Também não é viável nesta sessão

- Não há Windows nem os binários aqui.
- A instalação do REA (ferramenta de engenharia reversa) foi bloqueada pelo controle de permissões.
- Mesmo com acesso, o resultado não poderia ser usado, pelas licenças.

---

## 3. Método "sala limpa" (como vamos fazer)

**Fontes permitidas:**
- páginas oficiais de recursos e manuais públicos dos fabricantes;
- análises publicadas (PCWorld e outras);
- documentação da Microsoft (Win32, WMI/CIM, Windows Update Agent, WinGet, NTFS, ETW);
- formatos abertos com licença compatível (winapp2.ini, CC BY-SA 4.0).

**Proibido:**
- descompilar, desmontar ou inspecionar a memória dos programas;
- copiar textos, ícones, layout de telas ou bases de dados deles (o Logs Database do Revo, a base de drivers do CCleaner e as regras de limpeza internas do CCleaner);
- usar os nomes deles no produto.

**Registro:** cada função nova cita no código e no PR a documentação pública usada. Isso é a prova de origem independente.

**Comparação:** só interna, medindo o nosso próprio resultado (por exemplo, quantas sobras o nosso motor encontra contra o que a instalação criou). Não publicaremos comparativos com os concorrentes.

---

## 4. Inventário de funções e viabilidade

Legenda:
- **Situação no ND:** ✅ já existe · 🟡 existe em parte · ⬜ não existe
- **Viabilidade:** **A** (alta) · **M** (média, com limites) · **N** (não faremos)
- **Esforço** (fração de leva): P (até ¼) · M (até ½) · G (1 leva ou mais)

### 4.1 Revo Uninstaller Pro 5.5.4 (página oficial do produto)

| # | Função (nome do fabricante) | O que faz | Situação no ND | Viab. | Como faremos (resumo) | Esforço |
|---|---|---|---|---|---|---|
| R01 | Uninstaller + varredura de sobras ("3-mode leftover removal") | Roda o desinstalador do programa e procura arquivos, pastas e chaves de registro que sobraram | 🟡 Espaço › Aplicativos só abre o desinstalador oficial (A29) | A | Motor de sobras com 3 níveis de confiança, revisão antes de apagar e backup (seção 5.2) | G (2 levas) |
| R02 | Forced Uninstall | Remove restos de programas cujo desinstalador sumiu ou quebrou, ou que já foram removidos | ⬜ | A | O mesmo motor, alimentado por nome, pasta ou executável | M |
| R03 | Quick/Multiple Uninstall | Vários programas em fila: um ponto de restauração e um backup só, desinstalação silenciosa e sobras automáticas | ⬜ | A | Fila com `QuietUninstallString`, MSI `/qn` e detecção do tipo de instalador. Sem modo silencioso, a desinstalação é interativa, uma por vez | M |
| R04 | Forced MSI Uninstall | Força a remoção (e não o reparo) de pacotes MSI | ⬜ | A | `msiexec /x {ProductCode}` | P |
| R05 | Real-Time installation monitor (trace) | Registra o que um instalador muda no sistema, para desinstalar com precisão depois | ⬜ | M | Registro: foto antes e depois. Arquivos: diário USN do NTFS. Exige administrador; outros programas abertos geram ruído (seção 5.4) | G (2 levas) |
| R06 | Manage Installation Logs (Traced Programs) | Lista os traces; desinstalação completa ou parcial; renomear; exportar em TXT/HTML; levar para outro PC | ⬜ | A | Traces em JSON compactado, com exportação e importação | M |
| R07 | Advanced Uninstall (trace + varredura) | Desinstala pelo trace e ainda procura o que ele não pegou | ⬜ | A | Trace + motor de sobras | P |
| R08 | Logs Database | Traces feitos pelo laboratório do fabricante, guardados no servidor dele | ⬜ | **N** | É conteúdo proprietário e depende de laboratório e servidor. Alternativa futura: traces compartilhados por usuários, depois que existir o servidor da M18 | — |
| R09 | Windows Apps | Lista os apps da Store à parte; remove vários; remove para todos os usuários; redefine o app | ⬜ | A | PowerShell `Get/Remove/Reset-AppxPackage` e `Remove-AppxProvisionedPackage` (administrador). Apps de sistema e frameworks ficam protegidos | M |
| R10 | Browser Extensions (remover) | Lista e remove extensões de Chrome, Firefox, Edge, IE e Opera | 🟡 lista e abre a página do navegador (M31) | M | **Só abrir a página do navegador.** O Chrome e o Edge protegem as preferências com assinatura (Secure Preferences): mexer por fora faz o navegador "resetar" o perfil | P |
| R11 | Multi-level Backup System + Backup Manager | Ponto de restauração, backup do registro em cada desinstalação, arquivos para a Lixeira, restaurar itens apagados | 🟡 ponto de restauração (A36) e Ações com desfazer (A37) | A | Backup de cada item apagado (chaves em .reg; arquivos num zip próprio) e tela "Backups" com restaurar | M |
| R12 | Uninstall History | Histórico das desinstalações | 🟡 aba Ações | A | Filtro "Desinstalações" em Ações, com o resumo das sobras | P |
| R13 | Hunter Mode | Arrasta um alvo sobre uma janela, atalho ou ícone da bandeja para desinstalar, encerrar, tirar da inicialização, ver propriedades ou abrir a pasta | ⬜ | M | **Janelas:** `WindowFromPoint`. **Atalhos:** a pessoa arrasta o atalho para a janela do app. **Ícone da bandeja:** não, porque no Windows 11 a bandeja é XAML e não dá para identificar o ícone de forma confiável | M |
| R14 | Autorun Manager | Liga e desliga programas da inicialização | ✅ aba Inicialização (com Editor, M27/M28) | — | — | — |
| R15 | Junk Files Cleaner | Procura arquivos inúteis por extensão (.tmp, .chk, ~$…) | ⬜ | A | Busca com lista para revisão. `.bak` e `.old` desmarcados, porque podem ser da pessoa | M |
| R16 | Windows Tools | Atalhos para as ferramentas do Windows | ⬜ | A | Painel com ~20 atalhos (seção 5.13) | P |
| R17 | Browsers Cleaner | Histórico, cookies, sessão, formulários dos navegadores | 🟡 só o cache (A03) | A | SQLite do próprio Windows (`winsqlite3.dll`), com o navegador fechado (seção 5.6) | G |
| R18 | MS Office Cleaner | Listas de arquivos recentes (MRU) do Office | ⬜ | A | Chaves `File MRU` e `Place MRU` por versão e app, com backup .reg | P |
| R19 | Windows Cleaner | Rastros do Windows (recentes, Executar, pesquisas, área de transferência, temporários, Lixeira) | 🟡 temporários e Lixeira | A | Regras próprias (seção 5.5) | M |
| R20 | Evidence Remover | Apaga de vez o espaço livre do disco (dados já excluídos) | ⬜ | A | Preenche o espaço livre (1 passada, NIST SP 800-88). Em SSD não há garantia, e o app diz isso | M |
| R21 | Unrecoverable Delete | Apaga arquivos e pastas sem chance de recuperar | ⬜ | A | Sobrescreve, renomeia e apaga. Bloqueia OneDrive e pastas do sistema; avisa sobre SSD | M |
| R22 | Portable (Pro Portable) | Roda de um pendrive | ✅ B43 (`portatil.txt`) | — | — | — |
| R23 | Command Line Support | Linha de comando | 🟡 `--tab`, `--demo`, `--background` | A | `--tab=programas`, `--export-programs <arquivo>`. **Desinstalação pela linha de comando, não:** risco alto e pouco uso | P |
| R24 | Atualizações do Windows na lista | Lista as atualizações; o fabricante recomenda remover pelo próprio Windows | 🟡 histórico no diagnóstico (A22) | M | Listar e abrir "Desinstalar atualizações" do Windows. O `wusa /uninstall` não remove os pacotes combinados (SSU+LCU) | P |

### 4.2 CCleaner 7 Professional e Professional Plus

O site oficial recusa leitura automática, então a lista vem da PCWorld (lançamento do 7.0), de um comparativo de jan./2026, do histórico de versões citado em buscas e da Wikipedia. **Vale confirmar no app**, se você tiver uma licença.

**Professional Plus:** CCleaner Professional + Recuva + Speccy, para até 3 aparelhos. Antes também incluía o Defraggler, cujo fim de vida foi anunciado em setembro de 2026.

| # | Função | O que faz | Situação no ND | Viab. | Como faremos (resumo) | Esforço |
|---|---|---|---|---|---|---|
| C01 | Health Check (PC Health Check) | Análise geral com recomendações | ✅ Painel: nota, gargalo, diagnóstico com "Corrigir" | — | — | — |
| C02 | Limpeza personalizada (Windows e apps) | Temporários, sobras de instaladores, relatórios de erro, caches, Lixeira, documentos recentes; regras por aplicativo; apps da Store opcionais | 🟡 Limpeza com ~20 locais | A | Motor de regras compatível com o formato **winapp2.ini** (4.068 regras, CC BY-SA 4.0), regras próprias e os módulos da Limpeza de Disco do Windows (`cleanmgr /sagerun`) | G |
| C03 | Limpeza de navegadores | Cookies (com lista de sites a manter), histórico, cache, sessão, downloads, formulários | 🟡 só o cache | A | Igual à R17 | (R17) |
| C04 | Registro (registry cleaner) | Procura "entradas inválidas" no registro | ⬜ | M | **Versão restrita** (seção 5.10): só entradas com prova objetiva (apontam para arquivo que não existe), com backup e desfazer. A Microsoft não dá suporte a limpadores de registro (KB 2563254) e o ganho de desempenho é nulo; o app dirá isso | M |
| C05 | Software Uninstaller (motor novo no v7) | Remove vários de uma vez, apaga sobras automaticamente, remove "temporariamente" e restaura depois | 🟡 (A29) | A/M | R01–R03. "Restaurar app removido" a partir do backup, sem garantia para apps com serviços ou drivers | (R01) + M |
| C06 | Startup (inicialização, tarefas, menu de contexto, serviços) | Gerencia o que roda sozinho | 🟡 inicialização ✅, tarefas ✅ (M29), serviços ✅ (M30); **menu de contexto ⬜** | A | Gerenciador do menu de contexto do Explorador (seção 5.9) | M |
| C07 | Browser Plugins | Lista e gerencia extensões | 🟡 (M31) | M | Igual à R10 | — |
| C08 | Duplicate Finder (antes "Disk Analyzer", renomeado na 7.01) | Duplicados com miniaturas; manter o mais antigo ou o mais novo; Lixeira ou apagar | 🟡 Espaço › Arquivos duplicados | A | Miniaturas, regras de qual manter e envio para a Lixeira | M |
| C09 | Análise do disco por tipo (versões 6.x) | Espaço por categoria (imagens, vídeos, documentos…) | 🟡 pastas e maiores arquivos | A | Novo modo "Por tipo de arquivo" no Espaço | P |
| C10 | System Restore | Lista e apaga pontos de restauração | ✅ M37 (apaga os antigos) | A | Apagar um ponto escolhido | P |
| C11 | Drive Wiper / apagamento seguro | Apaga o espaço livre ou a unidade inteira | ⬜ | A/M | Espaço livre (R20). Unidade inteira **só externa e fora do sistema**, com `format X: /P:1` e confirmação digitando a letra | M |
| C12 | Performance Optimizer ("modo soneca") | Põe para dormir programas em segundo plano até a pessoa abrir | 🟡 modo eficiência e prioridade (Processos); serviços e tarefas separados | M | "Modo soneca" por aplicativo: desliga a inicialização, serviços (manual), tarefas e fecha os processos de fundo; acorda ao abrir o app (seção 5.9) | G |
| C13 | Driver Updater | Procura e instala drivers novos | 🟡 aviso de driver de vídeo antigo (A23) | M | **Só pelo Windows Update** (API do Windows Update Agent, `Type='Driver'`), com backup dos drivers e ponto de restauração. Não usaremos base de terceiros nem baixaremos de sites de fabricantes | M |
| C14 | Software Updater (com pular versão e pausar por app) | Atualiza programas desatualizados | ⬜ | A | **WinGet** (gerenciador de pacotes do Windows), pela API COM oficial (`Microsoft.WindowsPackageManager.ComInterop`), com reserva na linha de comando | G |
| C15 | Limpeza agendada | Limpa em horários definidos | 🟡 semanal, com o PC parado (A39) | A | Diária, semanal, mensal ou ao iniciar, com a escolha do que limpar | P |
| C16 | Monitoramento em tempo real ("Smart Cleaning") | Avisa quando o lixo passa de um limite; limpa o navegador ao fechar | ⬜ | A | Limite configurável (ex.: 1 GB); limpeza ao detectar que o navegador fechou | M |
| C17 | Remoção de rastreadores (privacidade) | Apaga cookies de rastreamento | ⬜ | M | Apagar cookies com uma lista de sites a manter. **Sem lista de rastreadores de terceiros:** as mais usadas têm licença não comercial ou GPL; isso fica para revisão | P |
| C18 | Suporte prioritário | — | — | N | É serviço, não função | — |
| C19 | Vários aparelhos (macOS, Android) | — | — | **N** | Fora do escopo (o app é WPF para Windows) | — |
| C20 | Suporte a Arm64 (novo no v7) | Roda nativo em PCs Arm | ⬜ | A | Publicar também `win-arm64` e testar as chamadas nativas | P |
| C21 | Tema claro, escuro e automático (v7) | — | ✅ (incluindo por horário, B37) | — | — | — |

### 4.3 Recuva (Professional Plus)

| # | Função | O que faz | Situação no ND | Viab. | Como faremos | Esforço |
|---|---|---|---|---|---|---|
| V01 | Recuperar arquivos apagados (NTFS, FAT, exFAT, ext; HD, pendrive, cartão) | Encontra e recupera arquivos excluídos | ⬜ | M | **Opção A (recomendada):** interface para o **Windows File Recovery** (`winfr`), gratuito e da Microsoft: modo normal para NTFS recente e modo extensivo para antigos, formatados, FAT e exFAT. Sem ext2/3/4. **Opção B:** motor próprio, lendo a MFT do NTFS só para leitura, com prévia (3 a 4 levas) | M (A) · G+ (B) |
| V02 | Prévia e chance de recuperação | Mostra miniaturas e o estado (excelente/ruim) antes de recuperar | ⬜ | M | Só na opção B. Com o `winfr`, os arquivos são recuperados para uma pasta e vistos depois | (B) |
| V03 | Apagar de vez os arquivos encontrados | Sobrescreve os arquivos apagados encontrados | ⬜ | A | Coberto pela limpeza do espaço livre (R20) | — |

### 4.4 Speccy (Professional Plus)

| # | Função | Situação no ND | Viab. | Como faremos | Esforço |
|---|---|---|---|---|---|
| S01 | Resumo e detalhes: sistema, CPU, RAM (pentes e slots), placa-mãe e BIOS, vídeo, discos (SMART), ópticos, áudio, periféricos, rede | 🟡 Painel: CPU, RAM, disco, bateria; saúde do disco em HealthProbe | A | Nova aba "Sistema" com WMI/CIM e tabela SMBIOS (`GetSystemFirmwareTable`) (seção 5.11) | G |
| S02 | Temperaturas | 🟡 zona térmica do Windows e disco | M | Disco e zona térmica (já existem) e GPU NVIDIA pela biblioteca do driver (`nvml.dll`). **Sem temperatura por núcleo da CPU:** exigiria driver de kernel de terceiros, e o WinRing0, usado por várias ferramentas, passou a ser detectado como ameaça pelo Defender em março de 2025 | M |
| S03 | Salvar ou exportar o retrato do sistema | 🟡 relatórios HTML/PDF do diagnóstico | A | TXT, HTML e PDF (o `SimplePdf` já existe). Sem publicar na internet | P |

### 4.5 Defraggler (Professional Plus antigo; fim de vida em set./2026)

| # | Função | Situação no ND | Viab. | Como faremos | Esforço |
|---|---|---|---|---|---|
| D01 | Analisar e desfragmentar unidade | 🟡 TRIM/otimização pelo Windows (A01) | A | `defrag.exe` / `Optimize-Volume` só em HD (em SSD, retrim) | P |
| D02 | Desfragmentar arquivo ou pasta | ⬜ | M | API de desfragmentação do Windows (`FSCTL_GET_RETRIEVAL_POINTERS` e `FSCTL_MOVE_FILE`), documentada, só HD e com administrador | M |
| D03 | Mapa da unidade | ⬜ | M | `FSCTL_GET_VOLUME_BITMAP` | M |
| D04 | Desfragmentação na inicialização (pagefile, MFT) | ⬜ | **N** | Exige rodar antes do Windows: risco alto e ganho pequeno hoje | — |

**Prioridade baixa:** o produto saiu de linha e a maioria dos notebooks usa SSD.

### 4.6 Placar

| | Revo | CCleaner | Recuva | Speccy | Defraggler | **Total** |
|---|---|---|---|---|---|---|
| Já existe | 2 | 3 | 0 | 0 | 0 | **5** |
| Viável (A) | 17 | 10 | 0 | 2 | 1 | **30** |
| Viável com limites (M) | 4 | 4 | 2 | 1 | 2 | **13** |
| Não faremos (N) | 1 | 2 | 0 | 0 | 1 | **4** |
| **Funções distintas** | 24 | 19 | 2 | 3 | 4 | **52** |

Três itens repetem uma função de outro produto e contam uma vez só: C03 = R17 (navegadores), C07 = R10 (extensões) e V03 = R20 (espaço livre). C05 e C11 contam como viáveis; a parte limitada de cada um está descrita na linha.

---

## 5. Desenho técnico por módulo

Princípios herdados das fases anteriores, que valem para tudo:
- **Lógica pura no Core** (`src/NotebookDoctor.Core`, testável no Linux) e **acesso ao Windows em `Services/`**.
- Toda ação que muda o sistema:
  - passa pelo **modo simulação** (`Simulate()`);
  - pede **confirmação** (`Dialogs.Confirm`);
  - entra em **Ações**, com **Desfazer** quando houver backup;
  - respeita o **modo empresa** (senha, B38) quando apaga ou desinstala.
- **Modo demonstração:** as telas novas mostram dados fictícios ou ficam só leitura.
- **Textos novos em `Loc.T`/`Loc.F`, com inglês e espanhol:** os testes da M13 quebram a CI se faltar tradução.
- **Administrador sob demanda:** o app continua `asInvoker`; quem precisa reabre como administrador ("Reabrir como administrador", já existente).
- **Dependências:** hoje o app não tem nenhum pacote NuGet. O plano mantém isso, com uma exceção a decidir (D2, WinGet).

### 5.1 Aba "Programas" (base)

**Fontes da lista:**
- Programas Win32:
  - `HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall` (visões de 64 e 32 bits);
  - `HKLM\Software\WOW6432Node\…\Uninstall`;
  - `HKCU\…\Uninstall`.
- Mesmo critério do Painel de Controle:
  - esconde `SystemComponent=1`, entradas com `ParentKeyName` (atualizações de programas) e entradas sem `DisplayName`;
  - a opção "Mostrar componentes do sistema" mostra tudo.
- Apps da Store: `Get-AppxPackage` (usuário atual) e `Get-AppxPackage -AllUsers` (administrador). Frameworks e `NonRemovable` ficam protegidos.
- Atualizações do Windows: histórico do Windows Update (já lido na A22) e `Win32_QuickFixEngineering`. Só listar.

**Colunas:** Nome, Editor, Versão, Tamanho, Instalado em, Tipo (Win32/MSI/Store/Atualização), Escopo (todos os usuários / só este usuário), Arquitetura (32/64).

**Tamanho:**
- vem de `EstimatedSize`;
- sem ele, é a pasta `InstallLocation` (já há `DiskUsage.FolderSize`);
- se a pasta só pôde ser medida em parte, mostra "≈".

**Recursos de tabela:** busca, ordenação, totais, colunas escolhíveis, menu de clique direito e ícones (`DisplayIcon`, com o cache de ícones de Processos).

**Menu:** Desinstalar…, Abrir pasta, Abrir no Editor do Registro (`LastKey` do regedit), Pesquisar na internet, Propriedades (todos os valores da chave).

**Exportar lista:** CSV e HTML (reaproveita o `ReportExporter`).

**Migração:** o modo "Aplicativos instalados" do Espaço vira um atalho para a aba Programas.

### 5.2 Desinstalação com varredura de sobras (o coração)

**Fluxo:**

1. **Retrato prévio (antes de rodar o desinstalador).** Muita sobra só pode ser identificada enquanto o programa ainda existe. Reunir:
   - `InstallLocation` e as pastas de `UninstallString` e `DisplayIcon`;
   - `ProductCode` (MSI);
   - nome da chave, `DisplayName` e `Publisher` normalizados;
   - lista dos `.exe` e `.dll` da pasta de instalação;
   - serviços cujo `ImagePath` aponta para a pasta;
   - tarefas agendadas cuja ação aponta para a pasta (a COM do Agendador já existe, M29);
   - atalhos (.lnk) do Menu Iniciar e da Área de Trabalho que apontam para a pasta (`IShellLink`, já usado na B21);
   - entradas de inicialização da pasta (`StartupManager`);
   - regras do Firewall com programa na pasta (`HNetCfg.FwPolicy2`);
   - CLSIDs e ProgIDs cujo servidor ou comando de abrir está na pasta.
2. **Ponto de restauração**, se a Proteção do Sistema estiver ligada (o `RestorePoint.EnsureAsync` já existe). O Windows cria no máximo 1 a cada 24 h por padrão; o app avisa quando não criou.
3. **Executar o desinstalador:**
   - **MSI:** `msiexec /x {ProductCode}`, ou `/qn` na fila silenciosa. Códigos `0`, `1605` (já não instalado), `1641` e `3010` (pede reinício) são tratados.
   - **EXE:** separar caminho e argumentos do `UninstallString`. Quando o caminho tem espaços e não está entre aspas, usar o maior prefixo que existe no disco.
   - **Silencioso** (só na fila e só quando a pessoa pede):
     - `QuietUninstallString` primeiro;
     - senão, detectar o tipo de instalador pela assinatura do arquivo: Inno Setup (`unins000.exe`, `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`), NSIS (`/S`), WiX Burn (`/uninstall /quiet`), Squirrel (`Update.exe --uninstall -s`);
     - InstallShield e desconhecidos: modo interativo.
   - **Esperar o fim de verdade.** O Inno Setup e o NSIS se copiam para `%TEMP%` e o processo original termina na hora. Por isso:
     - acompanhar a árvore de processos: filhos pelo PID pai, mais processos novos em `%TEMP%` que surgem na janela de tempo;
     - acompanhar a atividade do `msiexec`;
     - terminar quando a árvore esvazia por 5 s e a chave de desinstalação sumiu;
     - ou quando a pessoa clica em "O desinstalador terminou".
   - **Administrador:** programas em HKLM pedem UAC (`runas`). Na fila, o app sugere reabrir como administrador antes, para pedir o UAC uma vez só.
4. **Varredura de sobras** em três níveis, que a pessoa escolhe ao confirmar:

   **Seguro** (padrão; itens marcados):
   - tudo dentro de `InstallLocation`;
   - valores e chaves do registro que apontam para dentro dela: App Paths, Run, `ImagePath` de serviços, ações de tarefas, servidores de CLSID, comandos de ProgID, regras de firewall;
   - a própria chave de desinstalação, quando ficou órfã;
   - atalhos que apontam para a pasta.

   **Moderado** (marcados):
   - pastas com o nome exato do produto, ou `Editor\Produto`, em `ProgramData`, `AppData\Roaming`, `AppData\Local`, `LocalLow`, `Program Files (x86)` / `Program Files` (a outra visão) e Menu Iniciar;
   - chaves `HKCU|HKLM\Software[\WOW6432Node]\<Editor>\<Produto>` e `…\Software\<Produto>`.

   **Avançado** (desmarcados):
   - correspondência parcial com um termo do nome de 5 ou mais letras;
   - a pasta do editor, quando não há outro programa instalado do mesmo editor.

5. **Revisão:** árvore com o caminho, o motivo ("aponta para a pasta de instalação", "nome igual ao produto") e o nível, com a prévia de arquivos que já existe (`CleanPreview`).
6. **Backup e remoção:**
   - **Chaves e valores:** exportados para `.reg` antes de apagar.
   - **Arquivos:** copiados para o armazém de backups (zip por sessão).
   - **Pastas acima de 500 MB:** vão para a Lixeira, e o app avisa.
   - **Serviços:** param e são apagados (`sc stop` / `sc delete`), com backup da chave.
   - **Tarefas:** exportadas em XML e apagadas.
   - **Arquivos em uso:** `MoveFileEx(MOVEFILE_DELAY_UNTIL_REBOOT)`, e o app mostra "termina ao reiniciar".
7. **Registro em Ações**, com "Desfazer" (restaura `.reg`, arquivos, tarefas e serviços).
   - **Armazém de backups:** 30 dias ou 2 GB, configurável, com a tela "Backups" para restaurar ou apagar.

**Proteções (nunca apagar, em nenhum nível):**
- **Pastas:**
  - `Windows`, `System32`, `SysWOW64` e `WinSxS`;
  - a raiz de `Program Files`, `Common Files`, `ProgramData` e da pasta do usuário;
  - Documentos, Imagens, Músicas, Vídeos e Área de Trabalho, exceto `.lnk`;
  - OneDrive;
  - a pasta do próprio Notebook Doctor.
- **Registro:**
  - `HKLM\SOFTWARE\Microsoft`, `\Policies`, `\Classes` de tipos comuns (`txtfile`, `*`, `Directory`…) e `Windows NT`;
  - qualquer coisa em `HKLM\SYSTEM` que não seja um serviço identificado.
- **Itens compartilhados:**
  - itens dentro do `InstallLocation` de outro programa instalado;
  - DLLs com `SharedDLLs` acima de 1;
  - pastas do editor usadas por outro produto dele;
  - arquivos abertos por processos em execução.
- **Dados da pessoa:** pastas com arquivos que parecem ser dela (saves de jogos, documentos, projetos) entram desmarcadas e com aviso.

**Normalização de nomes (Core, com testes):**
- **Produto:** remove versão (`7-Zip 24.08 (x64)` → `7-Zip`), arquitetura e edição.
- **Editor:** remove sufixos (`Inc.`, `Ltd`, `GmbH`, `Corporation`, `Software`, `LLC`, `S.A.`).
- **Termos genéricos** ("Microsoft", "Windows", "Update", "Common", "Shared", "Setup", "Tools"…) nunca bastam sozinhos para uma correspondência.

**Critérios de aceite (no corpus de testes, seção 6.2):**
- 0 falsos positivos no nível Seguro e no Moderado;
- no Moderado, encontrar pelo menos 90% do que a instalação criou e o desinstalador deixou.

### 5.3 Fila, remoção forçada, apps da Store e "Caçador"

**Fila (Quick/Multiple):**
- marcar vários programas;
- 1 ponto de restauração e 1 sessão de backup;
- silencioso só onde houver modo seguro;
- sobras no nível Moderado, aplicadas sem revisão só se a pessoa marcar "Remover sobras automaticamente";
- relatório no fim.

**Remoção forçada:** a pessoa informa um nome, uma pasta ou um `.exe`. O app monta o retrato a partir disso, sem desinstalador, e roda a varredura. Também serve para entradas quebradas da lista ("desinstalador não encontrado").

**Apps da Store:**
- remover (usuário atual);
- remover para todos os usuários e para contas novas (`Remove-AppxPackage -AllUsers` e `Remove-AppxProvisionedPackage`, administrador);
- redefinir (`Reset-AppxPackage`, Windows 10 21H1 ou mais novo, com reserva em abrir as configurações do app).

**Caçador:**
- botão "Mirar": arrastar o alvo sobre uma janela (`WindowFromPoint` → PID → executável → programa dono da pasta);
- ou arrastar um atalho para a janela do app;
- menu: Desinstalar, Encerrar, Tirar da inicialização, Abrir pasta, Propriedades.

**Atalhos de outras abas:** "Desinstalar o programa" no menu de Processos e de Inicialização, quando o dono é identificado.

### 5.4 Monitor de instalação (trace)

**Botão:** "Instalar monitorando…" (escolhe o instalador). Exige administrador.

**Antes:**
- **Registro:** fotografia de `HKLM\SOFTWARE` (64 e 32 bits), `HKLM\SYSTEM\CurrentControlSet\Services` e `HKCU\Software`. Guarda, por chave, os nomes, tipos e o hash de cada valor. Estimativa: 20 a 60 s, com progresso.
- **Arquivos:** guardar o `NextUsn` do diário USN de cada unidade NTFS fixa (`FSCTL_QUERY_USN_JOURNAL`). Em unidades sem NTFS, fotografar `Program Files`, `ProgramData`, `AppData` e o Menu Iniciar.

**Durante:**
- roda o instalador;
- espera a árvore de processos, como na 5.2;
- o app pede para fechar outros programas.

**Depois:**
- **Registro:** nova fotografia e diferença (criados, alterados com o valor antigo, apagados).
- **Arquivos:** ler o diário USN a partir do número guardado (`FSCTL_READ_USN_JOURNAL`), com motivos `FILE_CREATE`, `DATA_*`, `RENAME_NEW_NAME` e `FILE_DELETE`. Traduzir para caminho com `OpenFileById` + `GetFinalPathNameByHandle`.
- **Filtrar ruído** por uma lista fixa: Prefetch, `$Recycle.Bin`, `System Volume Information`, `Temp`, caches de navegadores, `SoftwareDistribution`, `Logs`, os dados do próprio app.

**Por que não ETW em tempo real:** o provedor de registro do kernel (Microsoft-Windows-Kernel-Registry) tem relatos de perder eventos. A fotografia do registro mais o diário USN é mais confiável e não tem dependências.

**Ligação ao programa:** pela chave de desinstalação criada durante o trace.

**O trace guarda:** itens criados, que serão removidos, e itens alterados, que só poderão ser revertidos se a pessoa escolher (ex.: `PATH`). Formato: JSON compactado, na pasta de dados.

**Gerenciar traces:**
- lista;
- desinstalar pelo trace (desinstalador oficial + itens criados ainda presentes + varredura de sobras);
- desinstalação parcial;
- renomear;
- exportar em TXT/HTML;
- exportar e importar para outro PC.

**Limites:**
- outro programa mudando o sistema durante a instalação aparece no trace; a revisão antes de apagar cobre isso;
- mudanças que só acontecem no reinício não entram.

### 5.5 Limpeza avançada (regras)

**Motor de regras (Core):** lê o formato **winapp2.ini**: `Detect`, `DetectFile`, `FileKey` (com `RECURSE` e `REMOVESELF`), `RegKey`, `ExcludeKey` e `SpecialDetect`.
- **Origem das regras:**
  - as próprias, embutidas;
  - o `Winapp2.ini` da comunidade (versão 260915, 4.068 regras, **CC BY-SA 4.0**), baixado sob demanda numa versão fixada e conferida por hash, com crédito em "Licenças e créditos".
- **Exibição:** só as regras de programas detectados no PC, todas **desmarcadas** por padrão.
- **Bloqueio:** regras que apagam senhas, preenchimento automático ou dados de jogo são bloqueadas por uma lista negra no Core.

**Rastros do Windows (regras próprias):**
- documentos recentes (`Recent\*.lnk` e `Explorer\RecentDocs`);
- listas de atalhos (`AutomaticDestinations` e `CustomDestinations`);
- Executar (`RunMRU`), caminhos digitados (`TypedPaths`) e pesquisas do Explorador (`WordWheelQuery`);
- caixas Abrir/Salvar (`ComDlg32\OpenSavePidlMRU` e `LastVisitedPidlMRU`);
- área de transferência e o histórico dela;
- cache DNS (`ipconfig /flushdns`);
- dumps de memória (`MEMORY.DMP` e `Minidump`, administrador).

**Office (R18):** `File MRU`, `Place MRU` e `User MRU\*\File MRU` de cada versão e app.

**Limpeza de Disco do Windows (administrador):** gravar `StateFlags` nos módulos de `VolumeCaches` e rodar `cleanmgr /sagerun:N`. Inclui instalação anterior do Windows (`Windows.old`), limpeza de atualizações, logs de atualização, pacotes de drivers antigos, arquivos de Otimização de Entrega e relatórios de erro. É o caminho seguro para o que só o Windows sabe apagar.

**Arquivos inúteis (R15):**
- padrões `*.tmp`, `*.temp`, `~$*`, `*.chk`, pastas `FOUND.nnn` e `*.dmp` antigos;
- `.bak` e `.old` desmarcados;
- nunca `desktop.ini`;
- exclui pastas protegidas (seção 5.2) e as pastas que a pessoa indicar.

### 5.6 Dados dos navegadores (histórico, cookies e outros)

**Leitura:** SQLite pelo `winsqlite3.dll`, que vem no Windows 10 e 11, chamado direto (`DllImport`), sem dependência.

**Navegador fechado:** o app detecta o processo e oferece "Fechar o navegador" (pede para fechar a janela; nunca encerra à força).

**Chromium (Chrome, Edge, Brave, Vivaldi, Opera), por perfil:**
- **Histórico:** arquivos `History` (com `-journal`), `Visited Links`, `Top Sites`, `Shortcuts` e `Network Action Predictor`.
- **Cookies:** `Network\Cookies`, com `DELETE … WHERE host_key NOT IN (lista a manter)`.
- **Sessões:** `Sessions\*`, com aviso de que "continuar de onde parou" deixa de funcionar.
- **Formulários:** tabelas de autofill do `Web Data`.
- **Senhas: nunca.** A opção não existe.

**Firefox:**
- **Histórico:** apagar com SQL as visitas (`moz_historyvisits`) e os lugares sem favorito. O arquivo `places.sqlite` não pode ser apagado inteiro, porque guarda os favoritos.
- **Cookies:** `cookies.sqlite`, com a lista de sites a manter.
- **Formulários:** `formhistory.sqlite`.
- **Sessão:** `sessionstore.jsonlz4`.

**Lista "sites a manter":** a pessoa escolhe a partir dos cookies existentes; o padrão é vazio.

**Aviso:** com a sincronização do navegador ligada, o histórico pode voltar da nuvem.

**Limpar ao fechar o navegador (C16):** detecta o fim do processo e aplica as regras escolhidas, sem janela.

### 5.7 Monitoramento e agendamento (C15/C16)

- **Monitoramento:** a cada hora, com o PC parado (`MaintenanceRules.CanRunIdleTask`), estima o lixo. Acima do limite (padrão 1 GB), mostra uma notificação com "Limpar agora".
- **Agendamento:** diário, semanal, mensal ou ao iniciar, com a escolha dos itens. A limpeza semanal atual vira um caso particular.

### 5.8 Atualizadores

**Programas (C14) pelo WinGet:**
- **Lista:** pacotes instalados reconhecidos no catálogo, com `IsUpdateAvailable`.
- **Por app:** "Pular esta versão" e "Pausar atualizações deste app", guardados nas configurações.
- **Atualizar:**
  - selecionados, em fila, silencioso;
  - com progresso e código de resultado;
  - registra em Ações (não há desfazer; o app avisa).
- **Contratos dos pacotes:** o app mostra os termos e a pessoa aceita na tela. **O app nunca aceita em nome dela.**
- **Requisito:** Instalador de Aplicativo (App Installer). Se faltar, o app mostra como instalar.
- **Validar no Windows:** o comportamento da API COM com o app em modo administrador. Reserva: `winget upgrade --id <id> --exact --silent`.
- **Limites:** apps fora do catálogo ou com versão desconhecida não aparecem. Apps da Store continuam com a Store.

**Drivers (C13) pelo Windows Update:**
- **Busca:** `IUpdateSearcher.Search("IsInstalled=0 and Type='Driver'")`, mais a busca de opcionais (`BrowseOnly=1`, combinação a validar).
- **Mostra:** dispositivo, versão e data atuais (`Win32_PnPSignedDriver`) e a oferecida. Só recomenda quando a oferecida é mais nova.
- **Antes de instalar:** ponto de restauração e backup do driver atual (`Export-WindowsDriver -Online` ou `pnputil /export-driver`).
- **Instalar:** `IUpdateDownloader` + `IUpdateInstaller` (administrador), tratando "reinício necessário".
- **Voltar ao anterior:** pelo backup (`pnputil /add-driver … /install`) ou pelo "Reverter driver" do Gerenciador de Dispositivos.

### 5.9 Menu de contexto e "modo soneca"

**Menu de contexto (C06):**
- **Fontes:** `ContextMenuHandlers` em `*`, `AllFilesystemObjects`, `Directory`, `Directory\Background`, `Drive` e `Folder` (HKLM e HKCU `Software\Classes`), mais os verbos estáticos `shell\*`.
- **Por item:** DLL e editor, com a mesma leitura de assinatura de Inicialização (M28).
- **Desativar:**
  - **verbo estático:** valor `ProgrammaticAccessOnly`, documentado, que esconde do menu;
  - **manipulador:** a chave vai para um backup e é apagada;
  - **reativar:** devolve a chave.
- **Ações:** registro e desfazer.
- **Limite:** os itens do menu novo do Windows 11 vindos de apps empacotados (MSIX) não ficam no registro e não são listados.

**Modo soneca (C12):**
- **Por aplicativo** (família de `AppGroups` + editor), o app junta:
  - entradas de inicialização;
  - serviços automáticos de terceiros;
  - tarefas agendadas;
  - processos de fundo sem janela.
- **Pôr para dormir:**
  - desliga a inicialização (StartupApproved);
  - serviços em manual (M30);
  - tarefas desativadas (M29);
  - fecha os processos de fundo, com confirmação.
  - Mostra quanto de RAM e CPU isso usava.
- **Acordar:** reativa tudo (desfazer em Ações).
- **Acordar ao abrir** (opcional): observa o início de processos (WMI `__InstanceCreationEvent` em `Win32_Process`, a cada 2 s, sem administrador). Quando o executável principal abre, reativa os componentes; quando ele fecha, volta a dormir.
- **Nunca adormece:** antivírus e segurança, drivers de áudio, touchpad e vídeo, sincronização (OneDrive, Dropbox, com aviso) e acesso remoto.

### 5.10 Verificador de registro restrito (C04)

**Categorias** (só as que têm prova objetiva):
1. entradas de desinstalação cujo desinstalador e pasta não existem (também aparecem como quebradas em Programas);
2. App Paths para `.exe` que não existe;
3. Run e RunOnce para arquivo que não existe;
4. `SharedDLLs` para arquivo que não existe;
5. fontes registradas cujo arquivo não existe (administrador);
6. comando "abrir" de ProgID apontando para `.exe` que não existe, só quando o ProgID não está associado a nenhuma extensão em uso.

**Fora, de propósito:** CLSID/ActiveX, bibliotecas de tipos, MUICache, ordem do Menu Iniciar e sons. Ou têm resolução complexa (WOW64, SxS, variáveis), ou não trazem ganho, ou o risco é alto.

**Segurança:** backup `.reg` por item, desfazer em Ações e modo simulação.

**Texto honesto na tela:** "Isto organiza o registro, mas não deixa o computador mais rápido."

### 5.11 Informações do sistema (aba "Sistema")

| Seção | Fonte |
|---|---|
| Sistema operacional | `Win32_OperatingSystem`, edição e build (`CurrentVersion`), ativação (`SoftwareLicensingProduct`), Secure Boot, TPM (`Win32_Tpm`, administrador) |
| Processador | `Win32_Processor` (núcleos, threads, caches, virtualização), frequência atual (já medida) |
| Memória | `Win32_PhysicalMemory` (pentes: fabricante, part number, velocidade), `Win32_PhysicalMemoryArray` (slots), SMBIOS tipo 17 (`GetSystemFirmwareTable('RSMB')`) |
| Placa-mãe e BIOS | `Win32_BaseBoard` e `Win32_BIOS` (versão e data) |
| Vídeo | `Win32_VideoController` e DXGI (`IDXGIAdapter` → VRAM real), driver (versão e data), temperatura NVIDIA (`nvml.dll`, quando presente) |
| Armazenamento | `MSFT_PhysicalDisk` + `StorageReliabilityCounter` (já usados), SMART ATA (`MSStorageDriver_FailurePredictData`, administrador), saúde NVMe (`IOCTL_STORAGE_QUERY_PROPERTY`) |
| Áudio, ópticos e periféricos | `Win32_SoundDevice`, `Win32_CDROMDrive`, `Win32_PnPEntity` (USB e Bluetooth) |
| Rede | adaptadores, IPs, gateway e DNS (`NetworkInterface`), Wi-Fi (API Native Wifi `WlanQueryInterface`: SSID, sinal, banda) |
| Bateria | já existe (A10) |

**Exportar:** TXT, HTML e PDF, e "Copiar resumo" (padrão da B18).

### 5.12 Apagamento seguro e recuperação

**Apagar arquivo (R21):**
- sobrescreve com 1 passada aleatória, com `FILE_FLAG_WRITE_THROUGH`;
- inclui os fluxos alternativos (ADS);
- renomeia para nome aleatório 3 vezes, zera o tamanho e apaga.
- **Bloqueia:** OneDrive (a cópia na nuvem fica) e pastas protegidas.
- **Avisa:**
  - arquivos comprimidos, esparsos ou criptografados pelo NTFS podem ter blocos antigos;
  - em SSD não há garantia, por causa do wear leveling; a recomendação é usar BitLocker.

**Espaço livre (R20/C11):**
- arquivos temporários enchem o espaço livre em blocos de 256 MB e são apagados no fim;
- tem progresso e cancelar;
- na unidade do sistema, o app avisa que o Windows pode reclamar de pouco espaço durante a operação.

**Unidade inteira (C11):**
- só unidades **removíveis ou externas** que não sejam a do sistema;
- `format X: /FS:NTFS /P:1`;
- a pessoa digita a letra e o nome da unidade para confirmar.

**Recuperar arquivos (V01), opção A:**
- detecta o `winfr.exe`; se faltar, abre a página dele na Microsoft Store;
- tela com origem, destino (outra unidade, obrigatório), modo (normal/extensivo) e tipos (documentos, fotos, vídeos → filtros `/n`);
- roda como administrador, mostra o progresso e abre a pasta no fim.

### 5.13 Ferramentas do Windows, desfragmentação e Arm64

**Ferramentas do Windows (R16):**
- Gerenciador de Dispositivos, Serviços, Agendador de Tarefas, Visualizador de Eventos, Monitor de Recursos, Informações do Sistema, Configuração do Sistema;
- Editor do Registro, Gerenciamento de Disco, Limpeza de Disco, Firewall, Restauração do Sistema, Variáveis de Ambiente, Opções de Energia;
- Programas e Recursos, Recursos do Windows, Conexões de Rede, Contas de Usuário, Diagnóstico de Memória;
- Política de Grupo, só nas edições que têm.

**Desfragmentação (D01–D03):**
- só para HD;
- análise e otimização pelo `Optimize-Volume`;
- arquivo ou pasta pela API de desfragmentação;
- mapa da unidade pelo bitmap do volume.

**Arm64 (C20):**
- publicar `win-arm64`;
- revisar as chamadas nativas (tamanhos de estrutura, sem código específico de x64);
- testar num PC Arm.

---

## 6. Plano de ação

### 6.0 Preparação (antes da Fase 5)

| Passo | O quê | Por quê |
|---|---|---|
| P1 | **Fazer o merge do PR klivfy/notebook-doctor#1** depois do seu teste, e abrir **um PR por fase** daqui para frente | O PR atual tem 244 arquivos. Os módulos novos apagam coisas, e merecem revisão em partes menores |
| P2 | **CI no Windows:** novo job `windows-latest` (o executor do GitHub roda como administrador) para testes de integração | Cria um "programa falso" (chave Uninstall em HKCU, pastas em AppData e ProgramData, valor Run, atalho e tarefa agendada), roda o motor de sobras, confere que encontra **só** esses itens e limpa tudo. Também testa backup e restauração |
| P3 | **Máquina de testes:** VM Windows 11 (Hyper-V ou VirtualBox) com snapshot limpo, e se possível uma Windows 10 22H2 | Desinstalar programas reais no seu PC não é bom lugar para testar |
| P4 | **Assinatura de código** (já pendente na Fase 0) | Desinstalar, apagar de vez e mexer no registro disparam a heurística de antivírus e o SmartScreen em executáveis sem assinatura |
| P5 | **M13, segunda leva** (traduzir as mensagens do código) | Para os textos novos já nascerem no padrão `Loc.T` com inglês e espanhol |

### 6.1 Fases e levas

**Andamento:** P1 ✅ (merge do PR #1) · P2 ✅ (job `windows` na CI, com teste de integração do desinstalador) · levas 5.1, 5.2a e 5.2b ✅.

Na 5.2, a parte que lê e apaga no registro e no disco foi para um projeto novo, `src/NotebookDoctor.Windows` (net8.0, só Windows, sem WPF). As regras ficaram no Core. Assim, o teste de integração roda de verdade na CI do Windows: cria um programa falso, acha as sobras, apaga com backup e restaura.

Cada leva segue o ritual das anteriores: implementar, compilar, testar no Core, atualizar o PR e o `MELHORIAS.md`, gerar o pacote de teste e listar o roteiro de testes no Windows.

#### Fase 5 — Programas (desinstalador completo)

| Leva | Entregas | Itens | Testes automatizados | Teste no Windows (você) | Aceite |
|---|---|---|---|---|---|
| **5.1** ✅ | Aba Programas: lista Win32/Store/atualizações, colunas, busca, totais, menu, ícones, exportar CSV/HTML; Espaço › Aplicativos aponta para ela | R23 (export), R24 (listar) | Core: parser de `UninstallString`, código MSI, regras de visibilidade (`SystemComponent`, `ParentKeyName`), Store, exportação. A normalização de nome e editor foi para a 5.2a, onde é usada; C09 foi para a 6.3 | Comparar a lista com "Aplicativos instalados" do Windows; exportar | Mesma contagem do Windows (±componentes de sistema) |
| **5.2a** ✅ | Retrato prévio, executor (MSI/EXE, espera da árvore de processos, UAC), ponto de restauração, armazém de backups | R01 (parte 1), R11 | Core: detecção do tipo de instalador, códigos de saída MSI, decisão de fim da árvore (com relógio simulado) | Desinstalar 5 programas de tipos diferentes (MSI, Inno, NSIS, Squirrel, por usuário) | O app sempre sabe quando terminou |
| **5.2b** ✅ | Motor de sobras (3 níveis, proteções), tela de revisão, remoção com backup, desfazer, tela "Backups", histórico | R01 (parte 2), R12 | Core: classificação por nível, todas as proteções (com casos negativos), compartilhamento entre programas. **CI Windows:** programa falso (P2) | Corpus da seção 6.2 | 0 falso positivo em Seguro/Moderado; ≥ 90% das sobras no Moderado |
| **5.3** | Fila silenciosa, remoção forçada, MSI forçada, apps da Store (remover, todos os usuários, redefinir), Caçador, "Desinstalar" em Processos e Inicialização | R02, R03, R04, R09, R13, C05 | Core: fila (ordem, falha no meio, cancelar); escolha do modo silencioso | Fila de 3 programas; remover um app da Store para todos; Caçador numa janela | Falha de um item não interrompe os outros e aparece no relatório |
| **5.4a** | Monitor de instalação: fotografia do registro, diário USN, diferença, filtro de ruído, trace salvo | R05 | Core: diferença de fotografias (criado, alterado, apagado), filtro de ruído, leitura de registros USN a partir de bytes de exemplo | Instalar 3 programas monitorando; conferir o trace | Trace sem ruído das pastas filtradas |
| **5.4b** | Gerenciar traces: desinstalar por trace, parcial, exportar e importar, TXT/HTML | R06, R07 | Core: serialização do trace, desinstalação parcial | Desinstalar pelo trace e comparar com a fotografia anterior à instalação | O sistema volta ao estado anterior, menos os itens alterados |

#### Fase 6 — Limpeza e privacidade

| Leva | Entregas | Itens | Testes automatizados | Teste no Windows | Aceite |
|---|---|---|---|---|---|
| **6.1** | Motor de regras (formato winapp2), regras próprias (Windows, Office, Explorador), Limpeza de Disco do Windows (`cleanmgr /sagerun`), arquivos inúteis | C02, R15, R18, R19 | Core: parser do formato com todos os campos, curingas, variáveis de ambiente, lista negra; regras em pastas falsas | Rodar com 10 apps comuns; `Windows.old` numa VM atualizada | Nenhuma regra apaga fora dos caminhos declarados |
| **6.2** | Dados dos navegadores (SQLite), lista de sites a manter, limpar ao fechar, monitoramento com limite, agendamento flexível | R17, C03, C15, C16, C17 | Core: montagem dos SQL, lista a manter, regras de agendamento; SQLite real em teste de integração (CI Windows) | Chrome, Edge e Firefox com 2 perfis; manter o login de 2 sites | Os sites mantidos continuam logados; favoritos do Firefox intactos |
| **6.3** | Espaço: por tipo de arquivo; duplicados com miniaturas, "manter mais antigo/novo" e Lixeira | C08, C09 | Core: escolha de qual manter, agrupamento por categoria | Pasta de fotos com cópias | Nunca apaga a última cópia de um grupo |

#### Fase 7 — Manutenção e desempenho

| Leva | Entregas | Itens | Testes automatizados | Teste no Windows | Aceite |
|---|---|---|---|---|---|
| **7.1** | Atualizador de programas (WinGet), pular versão, pausar por app | C14 | Core: regras de pular/pausar, ordenação, comparação de versões | Atualizar 3 apps (um com administrador) | Termos sempre aceitos pela pessoa; reserva na linha de comando funciona |
| **7.2** | Drivers pelo Windows Update, backup e volta do driver | C13 | Core: comparação de versão e data, escolha do que recomendar | VM com driver antigo; backup e volta | Ponto de restauração e backup antes de toda instalação |
| **7.3** | Menu de contexto e modo soneca (com "acordar ao abrir") | C06, C12 | Core: agrupamento por app, lista "nunca adormecer", decisões de acordar | Desativar 2 itens do menu; adormecer Steam ou Teams e abrir | Tudo volta com "Acordar" ou desfazer |
| **7.4** | Verificador de registro restrito | C04 | Core: cada categoria com casos positivos e negativos | Rodar numa VM com entradas quebradas criadas de propósito | Só aponta o que tem prova; desfazer restaura |

#### Fase 8 — Ferramentas e sistema

| Leva | Entregas | Itens | Testes automatizados | Teste no Windows | Aceite |
|---|---|---|---|---|---|
| **8.1** | Aba Sistema (todas as seções), temperaturas disponíveis, exportar | S01–S03 | Core: parser SMBIOS (tipo 17) com tabelas de exemplo, formatação | 3 PCs diferentes (desktop, notebook, VM) | Pentes, slots e VRAM iguais ao Gerenciador de Tarefas |
| **8.2** | Apagar arquivo de vez, limpar espaço livre, apagar unidade externa | R20, R21, C11 | Core: planejamento de blocos, bloqueios (OneDrive, sistema, SSD) | Pendrive: apagar e tentar recuperar com o `winfr` | Arquivo apagado não é recuperado (HD/pendrive) |
| **8.3** | Recuperação via Windows File Recovery | V01 | Core: montagem dos argumentos e leitura do progresso | Apagar e recuperar fotos num pendrive (FAT/exFAT) e no NTFS | Arquivos recuperados abrem |
| **8.4** | Ferramentas do Windows, desfragmentação de HD (análise, arquivo/pasta, mapa), build Arm64 | R16, D01–D03, C20 | Core: leitura de bitmap e retrieval pointers com dados de exemplo | HD real ou VHD dinâmico; PC Arm, se houver | Desfragmentar não roda em SSD |
| *8.5 (opcional)* | Motor próprio de recuperação NTFS (lista, prévia, chance de recuperação) | V01/V02 opção B | Core: parser de registros da MFT, runlists, atributos residentes, com imagens de teste | Imagens de pendrive com arquivos apagados | Só leitura na origem; nunca grava na mesma unidade |

**Estimativa:** Fase 5, 6 levas · Fase 6, 3 levas · Fase 7, 4 levas · Fase 8, 4 levas. **Total: 17 levas.** A 6.1, a 6.2 e a 7.3 são as maiores e podem virar duas cada, o que leva a cerca de 20. A 8.5, opcional, soma mais 3 a 4.

### 6.2 Corpus de testes do desinstalador

**Instalar e desinstalar** em VM limpa (Windows 11 e Windows 10), registrando a verdade com o próprio monitor (5.4) ou com fotografia manual:

| Tipo | Programas |
|---|---|
| MSI | 7-Zip (pacote MSI), Node.js, Adobe Acrobat Reader |
| Inno Setup | Git for Windows, VS Code (instalador por usuário) |
| NSIS | VLC, Notepad++, OBS Studio |
| Squirrel / Electron | Discord, Slack |
| WiX Burn | Python, .NET Desktop Runtime, Visual C++ Redistributable |
| Lançadores com serviço | Steam, Epic Games, Zoom (serviço/atualizador) |
| Drivers / painéis | NVIDIA App, Logitech G HUB (só observar) |
| Store | Spotify, WhatsApp, um app pré-instalado (Clipchamp) |

O tipo exato de cada instalador muda entre versões, então a primeira tarefa da 5.2a é confirmar e fixar as versões usadas.

**Métricas por programa:**
- itens criados pela instalação;
- itens deixados pelo desinstalador oficial;
- itens que o motor achou em cada nível;
- falsos positivos.

O resultado vira uma tabela no PR da Fase 5.

### 6.3 Riscos e mitigação

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| Apagar algo de outro programa ou do Windows | Média | **Alto** | Três níveis, proteções fixas, verificação de compartilhamento, revisão, backup e desfazer; corpus e CI Windows; 0 falso positivo como critério de aceite |
| Antivírus ou SmartScreen tratar o app como suspeito | Média | Alto | Assinatura de código (P4); sem drivers de kernel; sem injeção nem hooks; comportamento sempre iniciado pela pessoa |
| UAC repetido cansar a pessoa | Alta | Médio | Sugerir "Reabrir como administrador" antes de filas e de varreduras que precisam dele |
| Instaladores que não seguem padrão (terminam antes, abrem navegador, pedem reinício) | Alta | Médio | Espera por árvore de processos, botão "O desinstalador terminou", tratamento de reinício pendente |
| Navegadores mudarem o formato dos arquivos | Média | Médio | Código por versão de esquema; se algo não for reconhecido, o app pula e avisa, sem apagar |
| Regras do winapp2 agressivas | Média | Médio | Desmarcadas, lista negra, versão fixada e conferida por hash, prévia antes de apagar |
| WinGet ausente ou com comportamento diferente como administrador | Média | Baixo | Detecção, reserva na linha de comando, mensagem clara |
| Escopo grande atrasar o resto | Média | Médio | Fases independentes; dá para parar depois de qualquer leva com algo útil entregue |
| Questão de propriedade intelectual | Baixa | Alto | Método sala limpa (seção 3), sem nomes deles no produto, revisão jurídica antes de qualquer material comparativo |

### 6.4 O que não faremos

- Descompilar, desmontar ou copiar qualquer parte dos dois programas.
- O Logs Database do Revo (R08).
- Bases de drivers de terceiros ou download de drivers fora do Windows Update.
- Drivers de kernel de terceiros, para temperatura por núcleo ou qualquer outra coisa.
- "Limpador de registro" completo (CLSID/ActiveX e afins).
- Desfragmentação na inicialização (D04).
- Listas de rastreadores com licença não comercial.
- Versões para macOS ou Android (C19).
- Promessas numéricas de ganho ("34% mais rápido").

---

## 7. Decisões que dependem de você

| # | Decisão | Opções | Recomendação |
|---|---|---|---|
| D1 | Método | (a) sala limpa, como descrito · (b) não seguir | **(a)** |
| D2 | Dependência NuGet para o WinGet | (a) `Microsoft.WindowsPackageManager.ComInterop` (Microsoft) · (b) só a linha de comando do `winget`, sem dependência | **(a)**, com (b) como reserva. A saída de texto do `winget` muda com o idioma e entre versões |
| D3 | Recuperação de arquivos | (a) interface do Windows File Recovery · (b) motor próprio (+3–4 levas) · (c) não fazer | **(a)** agora; (b) só se houver demanda |
| D4 | Registro | (a) verificador restrito · (b) nenhum · (c) completo | **(a)** |
| D5 | Regras winapp2.ini | (a) usar (CC BY-SA 4.0, com crédito, baixado à parte) · (b) só regras próprias | **(a)** |
| D6 | Apps da Store | (a) PowerShell (como o HealthProbe) · (b) APIs WinRT (exige mudar o TFM para `net8.0-windows10.0.17763.0` e aumenta o executável) | **(a)** |
| D7 | Ordem das fases | 5 → 6 → 7 → 8, ou outra | **5 → 6 → 7 → 8** |
| D8 | Organização das telas | Novas abas "Programas", "Sistema" e "Ferramentas" (12 no total), ou juntar Sistema e Ferramentas numa aba só | **Programas, Sistema e Ferramentas**: o menu já tem modo compacto (B15) |
| D9 | PRs | Fazer o merge do PR klivfy/notebook-doctor#1 antes e abrir um PR por fase | **Sim** |

---

## 8. Fontes consultadas

- Revo Uninstaller Pro, página oficial (versão 5.5.4, recursos e comparação): https://www.revouninstaller.com/products/revo-uninstaller-pro/
- Revo Uninstaller Pro, compra e lista Pro: https://www.revouninstaller.com/buy-now-revo-uninstaller-pro/
- Revo Uninstaller, suporte e FAQ: https://www.revouninstaller.com/support/
- Licença do Revo Uninstaller (cópia): https://revo-uninstaller.soft112.com/eula.html
- Licença do CCleaner/Piriform (cópia): https://github.com/yuliskov/windows-tweaks/blob/master/CCleaner/License.txt
- CCleaner, EULA e área jurídica: https://www.ccleaner.com/legal/end-user-license-agreement e https://www.ccleaner.com/legal
- CCleaner Professional Plus (página oficial, só pelo resumo da busca): https://www.ccleaner.com/knowledge/ccleaner-professional-plus
- PCWorld, lançamento do CCleaner 7: https://www.pcworld.com/article/2936657/ccleaner-7-arrives-with-a-new-design-and-smarter-pc-cleaning-features.html
- Comparativo Free × Pro × Pro Plus (jan./2026): https://mundobytes.com/en/Comparison-of-free-vs-pro-versions-of-CCleaner/
- Histórico de versões do CCleaner: https://www.ccleaner.com/ccleaner/version-history
- Wikipedia: Recuva, Speccy e Defraggler (fim de vida em set./2026): https://en.wikipedia.org/wiki/Recuva · https://en.wikipedia.org/wiki/Speccy · https://en.wikipedia.org/wiki/Defraggler
- Winapp2.ini (CC BY-SA 4.0): https://github.com/MoscaDotTo/Winapp2
- Microsoft, política sobre limpadores de registro (KB 2563254): https://support.microsoft.com/kb/2563254
- Microsoft, `IUpdateSearcher::Search` (critério `Type='Driver'`): https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iupdatesearcher-search
- WinGet, especificação da API COM: https://github.com/microsoft/winget-cli/blob/master/doc/specs/%23888%20-%20Com%20Api.md
- Microsoft, Windows File Recovery: https://support.microsoft.com/en-us/windows/experience/backup-recovery/windows-file-recovery
- Defender detectando o WinRing0 (mar./2025): https://www.neowin.net/news/windows-1110-is-flagging-winring0-on-your-pc-monitoring-fan-control-apps-heres-why/
- Confiabilidade do provedor ETW de registro: https://docs.velociraptor.app/artifact_references/pages/windows.etw.registry
- SQLite do Windows (`winsqlite3`): https://www.nuget.org/packages/SQLitePCLRaw.provider.winsqlite3
- Microsoft, registro de extensões do Shell: https://learn.microsoft.com/en-us/windows/win32/shell/reg-shell-exts
