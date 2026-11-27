# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# Notebook Doctor: guia para agentes

App de bandeja para Windows (WPF, .NET 8) que mostra o que deixa o notebook lento e corrige com segurança. Interface em português do Brasil, com inglês e espanhol. Produto da Klivfy.

## Onde fica cada coisa

- `src/NotebookDoctor`: o app WPF (janelas `*.xaml`, `MainWindow.*.cs` por aba, `Dialogs.cs`, `Smoke.cs`), `Services/` (o que fala com o Windows a partir do app), `Themes/` e `Textos/`. A solução é `NotebookDoctor.sln`, na raiz.
- `src/NotebookDoctor.Core`: regras puras, sem WPF nem chamadas ao Windows. Testável em qualquer sistema.
- `src/NotebookDoctor.Windows`: chamadas ao Windows (registro, arquivos, volumes, Lixeira, backups).
- `tests/NotebookDoctor.Tests`: xUnit. `[WindowsFact]` marca testes que mexem no sistema de verdade (rodam só no job Windows da CI).
- `Themes/` e `DESIGN.md`: sistema de design (tokens, componentes, como conferir mudança visual).
- `MELHORIAS.md`: andamento das levas e pendências. `Textos/Novidades.txt`: o que a pessoa vê como novidades.

## Regras que não podem quebrar

1. **Sala limpa.** Nunca descompilar nem copiar produtos de terceiros. Nomes de concorrentes não aparecem no produto: código, textos, telas e mensagens de commit.
2. **Todo texto visível passa pela tradução.** A chave é o texto em português: `Loc.T("...")`, ou `Loc.F("... {0}", valor)` quando há valores (nunca interpolação `$"..."` em texto de tela). Plurais com `Pt.N(n, "um", "muitos")`. Cada chave nova entra em `src/NotebookDoctor.Core/Idiomas/en.json` e `es.json`, com os mesmos `{0}` e as mesmas teclas de acesso (`_`). O `LocTests` falha se faltar, inclusive o texto fixo passado a `ShowSnack`, `Simulate`, `Notify`, `Alert`, `ActionLog.Add` e aos diálogos. Texto montado com `$"..."` nunca é traduzido: use `Loc.F`. Os textos longos do Sobre ficam em `Textos/<nome>.en.txt` e `.es.txt`, com a mesma estrutura do `.txt` em português.
3. **Nada destrutivo sem rede de segurança.** Confirmação (Cancelar como padrão), modo simulação respeitado, backup antes de apagar (`BackupStore`) e registro em Ações para desfazer. Senhas, cookies, preenchimento automático, favoritos e dados de jogos nunca entram em limpeza.
4. **Exclusões não seguem links.** Varreduras pulam junções (`AttributesToSkip = ReparsePoint`); exclusões em pastas onde outros usuários escrevem passam por `SafeDelete`.
5. **Visual pelo sistema de design.** Cores só por `DynamicResource` das chaves de `Themes/Light.xaml` e `Dark.xaml` (as duas com as mesmas chaves); tamanhos de texto pelos tokens `Fs*`; espaços em múltiplos de 4. Detalhes em `DESIGN.md`.
6. **Privacidade.** O app não tem telemetria ligada por padrão: a única é a das estatísticas anônimas opcionais (M18, Configurações › Privacidade, desligadas até a pessoa ligar, só contagens de `Telemetry.Tools`, sem identificador). Qualquer acesso novo à internet precisa de opção, aviso e atualização de `Textos/Privacidade.txt` (e das versões `.en` e `.es`).

## Como validar antes do commit

No Linux (sessões na nuvem), com o SDK em `/root/.dotnet` quando ele não estiver no PATH:

```bash
dotnet build NotebookDoctor.sln -c Release -p:EnableWindowsTargeting=true
dotnet test tests/NotebookDoctor.Tests -c Release -p:EnableWindowsTargeting=true
```

Um teste só (classe ou método): `dotnet test tests/NotebookDoctor.Tests -c Release -p:EnableWindowsTargeting=true --filter "FullyQualifiedName~LocTests"`.

Não há linter à parte: `Directory.Build.props` liga `TreatWarningsAsErrors` com `AnalysisLevel latest-recommended`, então qualquer aviso quebra o build. As regras de estilo puro já estão rebaixadas para sugestão no `.editorconfig`; não rebaixe outra só para passar.

A CI (`.github/workflows/ci.yml`) roda os testes no Linux e no Windows, publica para Arm64 e abre todas as telas com `NotebookDoctor.exe --demo --smoke=pasta`. O artefato **telas** traz as imagens e o `smoke.txt`, que lista ligação quebrada, texto sem tradução e texto com cara de português na interface em inglês. O job `capturas` (fotos de divulgação em português) só roda quando o título do PR tem `[capturas]`.

## O que exige ler mais de um arquivo

- **Edição da Microsoft Store.** `-p:StoreBuild=true` define `STORE` (em `Directory.Build.props`) e `Edition.IsStore` (Core) passa a ser verdadeiro. O que a certificação da Loja não aceita (atualizador próprio, WinGet, drivers) fica atrás de `Edition.*` e é escondido em `MainWindow.ApplyEdition`. A CI não compila essa edição: confira com `dotnet build ... -p:StoreBuild=true` e cubra o gate com teste. Pacote e textos da Loja ficam em `packaging/store/` (`LEIA-ME.md`), e `scripts/release/empacotar-msix.ps1` gera os `.msix` (precisa do Windows SDK, não roda no Linux).
- **Verificação visual (`Smoke*.cs`).** Janela ou ferramenta nova entra na lista `Tools(...)` de `Smoke.cs`; sem isso a CI não a fotografa nem confere a tradução dela. O que só existe num Windows de verdade fica em `Smoke.Windows.cs`.
- **Testes de tela no Linux.** Não dá para abrir WPF no Linux, então vários testes leem o código-fonte (`RepoPaths.App`) e conferem nomes e chamadas. `[WindowsFact]` só roda no job Windows.

## Fluxo de uma leva

1. Implementar com testes (Core primeiro; `[WindowsFact]` para o que toca o sistema).
2. Traduções em `en.json` e `es.json` (skill `traducoes`, com `scripts/traducoes.py`).
3. Uma linha em `Textos/Novidades.txt` (no topo) e o andamento em `MELHORIAS.md`.
4. Commit em português, descrevendo o que muda para a pessoa e o porquê; push; conferir a CI e as telas (skill `telas`, com `scripts/telas.py`).
