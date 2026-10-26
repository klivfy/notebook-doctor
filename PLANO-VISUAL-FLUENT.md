# Plano de redesign visual: Notebook Doctor na linguagem do Windows 11

Pedido: aproximar o visual do Notebook Doctor do Microsoft PC Manager (5 capturas: Início, Proteção, Armazenamento, Verificação de integridade e o relatório). Apoio: skills `apple-design` (princípios e fundamentos da HIG; o app é Windows, então valem acessibilidade, cor, tipografia, layout e escrita, não as convenções de iOS/macOS), `design:design-system` (auditoria de tokens e componentes) e `engineering:code-review` (estado do código de UI).

> **Regra de sala limpa.** O PC Manager usa a linguagem pública do Windows 11 (Fluent 2 / WinUI). O plano copia **a linguagem**, não o produto: os valores vêm dos recursos de tema públicos do WinUI (`microsoft-ui-xaml`, licença MIT) e das diretrizes de design do Windows. **Não entram:** o logotipo, as ilustrações 3D (placa-mãe, laptop, confete), os ícones próprios, os textos, o nome "PC Manager" e o fluxo idêntico de telas. Os ícones continuam da fonte do sistema (Segoe Fluent Icons), e as ilustrações, se houver, serão próprias.

## Andamento

| Leva | Situação | Commit |
|---|---|---|
| Verificação visual (`--demo --smoke`) na CI, com imagens de todas as telas | ✅ | `34f73d6` |
| V1 · Fundamentos | ✅ tokens Fluent, Segoe UI Variable, Mica, `Styles.xaml` dividido, teste de contraste (80 pares) | `b0a5645` |
| V2 · Controles | ✅ botões, caixas, interruptor, campos, combo, tabelas, menus, barra de progresso | `beee0bf` |
| V3 · Moldura | ✅ trilho de navegação com ponto de pendência, camada de conteúdo, título de 28 px, aviso no estilo InfoBar | `9cad52b` |
| V4 · Início | ✅ cartão da nota de saúde com ação larga, menu "Mais", atalhos 2 × 2, diagnóstico com ícones de estado | `9cad52b` |
| V5 · Seções e janelas | ✅ janelas de ferramenta com título de 28 px e fundo da moldura; cartões-linha na Limpeza; tradução das janelas corrigida | `3460def` |
| V6 · Polimento | ✅ animações 167/250 ms, mini-widget, `DESIGN.md` | `5f1c2ba` |
| Decisões V-D1 a V-D6 | ⏳ com você (seção 7); o app usa a recomendação de cada uma até lá | — |

**Fora do escopo do redesign, visto pela verificação visual:** textos montados no código do Painel (chip "n pontos de atenção", linha "Ligado há…", estado do disco e da bateria, nota "Regular/Bom") continuam em português com o app em inglês ou espanhol. É a pendência "M13, mensagens do código" do `MELHORIAS.md`; o `smoke.txt` da CI agora lista esses textos.

**Ainda não feito do plano:** migrar as 464 margens numéricas para os tokens de espaço (os tokens existem; a troca, tela por tela, fica para quando cada tela for mexida) e passar as outras abas (Espaço, Ajustes, Sistema, Configurações) para cartões-linha.

---

## 1. O que as capturas mostram (medido nos pixels, tema escuro)

| Camada | Cor medida | Equivalente público do WinUI |
|---|---|---|
| Moldura e trilho de navegação | `#151C26` (varia com o papel de parede) | Mica, sem cor própria |
| Camada de conteúdo (canto superior esquerdo arredondado) | `#21262D` | `LayerFillColorDefault` sobre Mica |
| Cartão | `#2C3138`, contorno de 1 px um pouco mais claro | `CardBackgroundFillColorDefault` + `CardStrokeColorDefault` |
| Cartão de estado "normal" | `#2F3939` (cartão puxado para o verde) | tom de sucesso a ~5% |
| Botão de destaque e indicador da navegação | `#60CDFF`, texto preto | `AccentFillColorDefault` (escuro) |
| Valor ou link em destaque ("11 Aplicativos", "129,8 GB") | `#99EBFF` | `AccentTextFillColorPrimary` (escuro) |
| Texto principal / secundário | `#FFFFFF` / `#D1D3D4` | `TextFillColorPrimary` / `TextFillColorSecondary` |
| Número de atenção ("2,6 GB", "Otimização sugerida") | `#FCE100` | `SystemFillColorCaution` (escuro) |
| Círculo de ícone azul / verde | `#22424F` / `#284E2F` | cor de estado a ~15% sobre o cartão |

Contrastes (WCAG, calculados dos hex): texto principal no cartão **13,1:1**; secundário **8,7:1**; valor em destaque **9,8:1**; amarelo de atenção **9,9:1**; preto no botão de destaque **11,7:1**. O trilho da barra de memória (`#3F444A`) tem só 1,33:1 sobre o cartão, mas é decoração: o que carrega a informação é o preenchimento `#60CDFF`.

**Padrões que se repetem:**

1. **Três planos:** moldura com Mica → camada de conteúdo (um degrau mais clara, canto de 8 px) → cartões (mais um degrau, canto de 8 px, contorno de 1 px). Sem sombras.
2. **Navegação em trilho compacto** (~80 px): ícone de 20 px **sobre** o rótulo de 12 px; o item ativo tem ícone e texto na cor de destaque e uma barrinha vertical de 3 × 16 px à esquerda; **ponto amarelo** quando a seção tem algo pendente; Configurações e Comentários no rodapé.
3. **Título de página grande** (28 px semibold: "Proteção do sistema", "Gerenciamento de armazenamento") e mais nada no cabeçalho.
4. **Cartão-linha** (o componente mais usado): ícone de 20 px à esquerda, título 16 px semibold, descrição 14 px secundária, e à direita **um** destes: botão de destaque, chevron `›` (abre detalhe) ou ícone de link externo `↗` (abre fora do app).
5. **Ladrilhos 2 × 2** no Início: título 18–20 px semibold, ícone de contorno no canto superior direito, rótulo secundário e o valor na cor de destaque (clicável).
6. **Cartão-herói de status** no topo, com a **ação principal** (Aumentar / Otimizar agora) larga e em destaque.
7. **Resumo da verificação:** grade de 2 colunas; cada item com caixa de seleção, ícone num círculo tonal, **número colorido** ("535 rastreamentos…") e chevron. Itens "normais" ficam sem caixa e com o fundo levemente verde.
8. **Tipografia Segoe UI Variable** e escala do Windows: 12 / 14 / 16–20 / 28 / 40.
9. **Botões retangulares** (cantos de 4 px), altura 32–40 px; a ação principal é a única preenchida.

---

## 2. Onde o Notebook Doctor está hoje

### 2.1 Auditoria do sistema de design (`design:design-system`)

**Resumo:** 3 temas de cor (claro, escuro, alto contraste), 40 estilos em um arquivo, 16 janelas. **Nota: 68/100.** A base é boa (tokens por função, `DynamicResource`, densidade, escala de texto do Windows); o que falta é **escala de raio, de espaçamento e de elevação**, e a linguagem é Material 3, não Fluent.

| Categoria | Definidos | Valores soltos no XAML |
|---|---|---|
| Cores | 31 pincéis por tema (`Surface`, `Primary`, `Level*`, `Chart*`…) | **0** hex nas janelas ✅ |
| Tipografia | 7 tamanhos (`FsLabel` … `FsHero`), 4 famílias | 1 (`MiniWidget.xaml:22`, `FontSize="10"`) |
| Raio | **nenhum token** | 9 valores diferentes: 2, 3, 4, 6, 8, 10, 16, 20, 22, 24 |
| Espaçamento | `CardPad`, `CardGap`, `RowMin` | **464** `Margin` numéricos nas janelas |
| Elevação | nenhum token | 3 `DropShadowEffect` (`MainWindow.xaml:1212`, `MiniWidget.xaml:12`, `Styles.xaml:338`) |
| Movimento | nenhum token | duração fixa de 450 ms em `Motion.cs` |

| Componente | Estados | Variantes | Comparado ao Fluent |
|---|---|---|---|
| Botão | ✅ hover, pressionado, desabilitado, foco | preenchido, tonal, texto, perigo, ícone | pílula de 20 px de raio (Material); Fluent usa 4 px e botão "padrão" com fundo sutil + contorno |
| Cartão | — | 1 | raio 16 px, sem contorno no claro/escuro; Fluent: 8 px + contorno de 1 px |
| Navegação | ✅ | expandida / compacta (só ícone) | pílula de 44 px com ícone ao lado do texto, 200 px de largura; falta o trilho "ícone sobre rótulo" e o ponto de aviso |
| CheckBox | ✅ | normal, de tabela | 18 px, raio 2; Fluent: 20 px, raio 4, preenchimento de destaque |
| Campo de texto / combo | ✅ | — | raio 6, contorno inteiro; Fluent: raio 4 e **sublinhado** de destaque no foco |
| Barra de progresso | ✅ | — | 8 px com contorno; Fluent: 3–4 px arredondada, sem contorno |
| Tabela | ✅ | — | linha com raio 10; Fluent: linhas retas, cabeçalho só texto |
| Aviso flutuante (snackbar) | ✅ | — | Material; Fluent: `InfoBar` no topo do conteúdo, com ícone de severidade |
| Cartão-linha / ladrilho / herói de status | ❌ | — | **não existem** como componentes; cada tela monta à mão |

### 2.2 Revisão de design (`apple-design`)

**Avaliação: Bom.** Os três críticos da revisão anterior (`REVISAO-DESIGN.md`: foco do teclado, conteúdo invisível ao Narrador, texto que não escalava) já foram corrigidos. O que separa o app do visual do Windows 11 são escolhas de linguagem, não defeitos.

- **[Alta] Parece um app Material em Windows.** Pílulas de 20 e 22 px, cartões de 16 px, Google Sans Flex: `branding.md` diz que um componente reestilizado deve manter "familiar sizing, placement, and behavior"; no Windows 11 a referência familiar é Fluent. *Correção:* raio 4/8, Segoe UI Variable, botão padrão com contorno.
- **[Alta] Sem hierarquia de planos.** Janela, cabeçalho e conteúdo têm o mesmo fundo (`Surface`), e os cartões flutuam sem contorno. `layout.md`: agrupar e usar "visual hierarchy" para mostrar o que vem junto. *Correção:* moldura (Mica) → camada de conteúdo → cartões com contorno.
- **[Média] Cor de destaque com dois papéis no claro.** `#00188F` (azul-marinho do Metro) em botões e gráficos; `color.md`: "Avoid using the same color to mean different things." *Correção:* destaque só para ação, seleção e valores clicáveis; séries de gráfico com cores próprias.
- **[Média] Janelas secundárias com título de 16 px** (`TitleMedium`) nas 12 janelas de ferramenta: o título da tarefa pesa o mesmo que um título de seção. *Correção:* título de página de 28 px, como as telas do Windows.
- **[Média] Painel denso.** Nota, gargalo, quatro links de exportação, saúde do disco e bateria, três métricas e o diagnóstico competem no primeiro cartão. `design-principles.md › Simplicity`. *Correção:* herói com uma ação, ladrilhos para o resto, exportações num menu "Mais".
- **[Baixa] Sombras soltas** em 3 lugares, num sistema que não usa elevação.

**O que funciona e deve ficar:** tokens por função e troca de tema sem reiniciar; anel de foco do teclado; escala de texto do Windows (`TextScale.Apply`); alto contraste; confirmações com verbo específico; segurança como design (desmarcado por padrão, Desfazer, simulação). **O elemento-assinatura continua sendo a nota de saúde com o pulso:** no visual novo, ela vira o cartão-herói (o lugar onde o PC Manager põe "Melhoria de desempenho").

### 2.3 Revisão do código de UI (`engineering:code-review`)

| # | Onde | Achado | Categoria |
|---|---|---|---|
| 1 | `Themes/Styles.xaml` (639 linhas) | Todos os estilos num arquivo só; trocar a linguagem mexe em tudo de uma vez. Separar em `Tokens.xaml`, `Controls.xaml`, `Components.xaml` | Manutenção |
| 2 | 12 janelas de ferramenta | O mesmo esqueleto repetido (título `TitleMedium`, texto `Muted`, conteúdo, `Msg`, Fechar) — 12 cópias para ajustar. Um `ToolWindowShell` (estilo de `Window` + `ContentControl` com cabeçalho) resolve em um lugar | Duplicação |
| 3 | 464 `Margin` numéricos | Sem escala; o redesign vai mudar espaçamentos e cada número é uma decisão solta. Introduzir tokens `Space4`…`Space32` (`Thickness`) e migrar por janela | Consistência |
| 4 | `ThemeManager.cs:112-125` | Pinta a barra de título com `DWMWA_CAPTION_COLOR`; com Mica isso precisa virar `DWMWA_SYSTEMBACKDROP_TYPE` (38) + modo escuro imersivo, com volta à cor sólida no Windows 10 | Correção |
| 5 | `ThemeManager.ApplyAccent` | Já lê a cor de destaque do Windows; para Fluent, derivar `AccentLight2` (escuro) e `AccentDark1` (claro) em vez de usar a cor crua, para manter 4,5:1 | Acessibilidade |
| 6 | `MiniWidget.xaml:22` | `FontSize="10"` fixo, fora da escala e do `TextScale` | Acessibilidade |
| 7 | `LocTests` | Os testes conferem os textos do XAML. Redesenhar sem mudar textos mantém os testes; textos novos (ex.: "Início") entram nas traduções na mesma leva | Risco de regressão |

**Veredito:** pronto para evoluir; os itens 1 a 3 são pré-requisito (preparam o terreno), 4 a 6 entram nas levas de fundamentos.

---

## 3. Sistema proposto (tokens)

### Cor

| Token | Escuro | Claro | Uso | Contraste |
|---|---|---|---|---|
| `WindowBackdrop` | Mica (sólido `#202020` sem Mica) | Mica (sólido `#F3F3F3`) | moldura e trilho | — |
| `LayerFill` | `#FFFFFF` 4% sobre a moldura (≈ `#272727`) | `#FFFFFF` 50% (≈ `#F9F9F9`) | camada de conteúdo | — |
| `CardFill` | `#FFFFFF` 5% (≈ `#2B2B2B`) | `#FFFFFF` | cartões | — |
| `CardStroke` | `#000000` 10% | `#000000` 6% (≈ `#E5E5E5`) | contorno dos cartões | decorativo |
| `TextPrimary` | `#FFFFFF` | `#1A1A1A` | texto | 13,1:1 / 17,4:1 |
| `TextSecondary` | `#FFFFFF` 79% (≈ `#D1D3D4`) | `#5D5D5D` | descrições | 8,7:1 / 6,6:1 |
| `Accent` (ação, seleção) | `#60CDFF`, texto preto | `#005FB8`, texto branco | botão principal, indicador | 11,7:1 / 6,3:1 |
| `AccentText` (valores clicáveis) | `#99EBFF` | `#003E92` | "11 programas", "129,8 GB" | 9,8:1 / 10,0:1 |
| `Caution` | `#FCE100` | `#9D5D00` | atenção, números a resolver | 9,9:1 / 5,3:1 |
| `Success` | `#6CCB5F` | `#0F7B0F` | normal, concluído | 6,5:1 / 5,4:1 |
| `Critical` | `#FF99A4` | `#C42B1C` | erro, perigo | 6,5:1 / 5,7:1 |

Os nomes atuais (`Surface`, `Primary`, `Error`…) ficam como apelidos dos novos durante a migração, para nenhuma tela quebrar no meio. Com "Usar a cor de destaque do Windows" ligado, `Accent` e `AccentText` vêm do Windows (derivados para manter o contraste). Os gráficos ganham séries próprias que não usam `Accent` nem as cores de estado.

### Tipografia (Segoe UI Variable, a fonte do Windows 11; no Windows 10, Segoe UI)

| Estilo | Tamanho / peso | Uso |
|---|---|---|
| `Caption` | 12 regular | rótulos da navegação, notas |
| `Body` | 14 regular | texto |
| `BodyStrong` | 14 semibold | rótulos fortes, cabeçalhos de tabela |
| `Subtitle` | 20 semibold | títulos de cartão-herói e ladrilhos |
| `CardTitle` | 16 semibold | título do cartão-linha |
| `Title` | 28 semibold | título da página e das janelas de ferramenta |
| `Display` | 40–56 semibold | só a nota de saúde |

Todos continuam multiplicados pelo "Tamanho do texto" do Windows. Google Sans Flex sai do texto corrido; pode ficar **só na nota de saúde** como marca (decisão V-D1).

### Forma, espaço, elevação, movimento

- **Raio:** `RadiusControl` 4 (botões, campos, caixas), `RadiusCard` 8 (cartões, camada de conteúdo, menus), `RadiusPill` 999 só para chips de estado.
- **Espaço:** escala de 4 — `Space4`, `Space8`, `Space12`, `Space16`, `Space24`, `Space32`. Cartão: 16 de preenchimento (20 no herói); 8 entre cartões-linha; 24 de margem da página.
- **Elevação:** contorno de 1 px em vez de sombra; sombra só em menus e no aviso flutuante.
- **Movimento:** 167 ms (rápido) e 250 ms (normal), curva de desaceleração; respeita "Efeitos de animação" desligado no Windows (já existe em `Motion.cs`).
- **Ícones:** Segoe Fluent Icons, 20 px nas linhas e na navegação, 16 px dentro de botões.

---

## 4. Componentes a criar ou refazer

| Componente | Base hoje | Como fica |
|---|---|---|
| `NavRail` | `NavList`/`NavItem` | Trilho de 80 px, ícone sobre rótulo (12 px), indicador de 3 × 16 px em `Accent`, ponto `Caution` de 8 px quando a seção tem pendência; modo expandido opcional (ícone + texto, 240 px) para quem prefere; Configurações no rodapé |
| `ContentLayer` | — | `Border` com `LayerFill`, canto superior esquerdo de 8 px, contorno `CardStroke` — envolve o título da página e o conteúdo |
| `PageHeader` | `PageTitle` + `SubTitle` + chips | Título `Title` 28 px; chip de estado e "Simulação ligada" à direita, discretos |
| `ActionRow` (cartão-linha) | montado à mão em cada aba | Ícone 20 · título 16 · descrição 14 · slot à direita (`Accent` button, `›` ou `↗`); a linha inteira é clicável quando o slot é `›`; teclado e Narrador como um botão |
| `Tile` | cartões de métrica | Título, ícone no canto, rótulo e valor em `AccentText` clicável; grade 2 × 2 que vira 1 coluna abaixo de 720 px |
| `StatusHero` | cartão da nota de saúde | Nota de saúde (assinatura) + frase do gargalo + **uma** ação principal larga ("Otimizar agora"); exportações num botão "Mais" |
| `CheckRow` (resumo do diagnóstico) | lista do diagnóstico | Grade de 2 colunas: caixa · ícone em círculo tonal · número colorido + frase · `›`; itens normais sem caixa e com fundo `Success` a 5% |
| `ToolWindowShell` | 12 cópias do esqueleto | Mica, título 28 px, ação principal no canto superior direito ou no rodapé (sempre no mesmo lugar), `InfoBar` para mensagens, Fechar no rodapé |
| `InfoBar` | snackbar | Barra no topo do conteúdo com ícone de severidade (informação, sucesso, atenção, erro) e ação opcional |
| Controles base | estilos atuais | Botão Fluent (padrão com contorno, destaque preenchido, sutil); `CheckBox` 20 px; `ToggleSwitch` para opções liga/desliga em Configurações; campo com sublinhado de foco; `ProgressBar` de 4 px; `DataGrid` com linhas retas; menus com raio 8 |

---

## 5. Telas

- **Painel → "Início".** Cartão-herói com a nota de saúde, o gargalo e "Otimizar agora"; faixa com memória em uso e lixo encontrado (barras finas, como no PC Manager); ladrilhos 2 × 2: **Diagnóstico** (n achados), **Processos** (n em execução), **Limpeza** (GB a liberar), **Inicialização** (n itens); o diagnóstico completo vira a tela de **Verificação** com o resumo em grade (item `CheckRow`) e "Corrigir selecionados" no topo.
- **Limpeza, Espaço, Ajustes, Sistema, Configurações:** colunas de `ActionRow` agrupadas por título de grupo (ex.: Limpeza › "Arquivos" com Limpeza avançada, Duplicados, Apagar de vez, Recuperar, Desfragmentar). As ferramentas que hoje são botões soltos ganham ícone, descrição e o mesmo lugar sempre.
- **Processos, Programas, Inicialização, Histórico, Ações:** tabelas no estilo Fluent dentro da camada de conteúdo, barra de busca e ações no topo.
- **12 janelas de ferramenta:** passam para `ToolWindowShell`; o conteúdo de cada uma não muda, só o esqueleto.
- **Mini-widget:** cantos de 8 px, contorno em vez de sombra, texto na escala.

```
┌───────┬────────────────────────────────────────────────────────┐
│  ⌂    │  Início                                     ● Saudável │
│ Início│ ┌────────────────────────────────────────────────────┐ │
│  ▤    │ │  82  Nota de saúde · pulso            [Otimizar ▸] │ │
│Process│ │  O disco está cheio: 9 % livre                     │ │
│  🧹 • │ │  Memória 29 % ▬▬▭▭▭      Lixo 2,2 GB ▬▬▬▭▭          │ │
│Limpeza│ └────────────────────────────────────────────────────┘ │
│  ◔    │ ┌───────────────────────┐ ┌──────────────────────────┐ │
│ Espaço│ │ Diagnóstico        ♡  │ │ Processos             ▣  │ │
│  …    │ │ 3 achados             │ │ 11 programas             │ │
│       │ └───────────────────────┘ └──────────────────────────┘ │
│  ⚙    │ ┌───────────────────────┐ ┌──────────────────────────┐ │
│Config.│ │ Limpeza            🧹 │ │ Inicialização         ⏻  │ │
└───────┴─┴───────────────────────┴─┴──────────────────────────┴─┘
  Mica      camada de conteúdo (canto de 8 px) · cartões com contorno
```

---

## 6. Plano de ação

Cada leva segue o ritmo de sempre: implementar, build sem avisos, testes, capturas nos dois temas (modo demonstração) e alto contraste, documentação, pacote de teste e confirmação antes da próxima.

| Leva | Entregas | Arquivos principais | Testes e verificação | Aceite |
|---|---|---|---|---|
| **V1 · Fundamentos** | Separar `Styles.xaml` em `Tokens`/`Controls`/`Components`; tokens de cor Fluent (claro e escuro) com apelidos dos nomes atuais; escala de raio, espaço e tipografia; Segoe UI Variable; Mica na janela principal (com volta ao sólido no Windows 10 e com transparência desligada); acento do Windows derivado | `Themes/*`, `ThemeManager.cs`, `App.xaml`, `TextScale.cs` | Testes do Core para a derivação de acento e os contrastes (todo par texto/fundo ≥ 4,5:1, gráficos ≥ 3:1); LocTests | Nenhuma tela quebra; contrastes medidos e registrados; alto contraste igual ao de hoje |
| **V2 · Controles** | Botões Fluent (padrão, destaque, sutil, perigo), `CheckBox`, `ToggleSwitch`, campos, combo, `ProgressBar`, rolagem, `DataGrid`, menus, anel de foco duplo do Windows, `InfoBar` no lugar do snackbar | `Themes/Controls.xaml`, `MainWindow.xaml.cs` (avisos) | Navegação só por teclado em todas as abas; Narrador lê botões, linhas e avisos | Mesma função, visual Fluent; foco visível em tudo |
| **V3 · Moldura** | `NavRail` (ícone sobre rótulo, indicador, ponto de pendência), `ContentLayer`, `PageHeader` de 28 px, modo compacto e expandido, mini-widget | `MainWindow.xaml`, `Themes/Components.xaml`, `MiniWidget.xaml` | Janela de 720 a 1920 px; texto a 200%; alto contraste | Navegação e títulos como no Windows 11; nada cortado |
| **V4 · Início e Verificação** | `StatusHero` com a nota de saúde e "Otimizar agora"; faixa de memória e lixo; ladrilhos 2 × 2; tela de Verificação com resumo em `CheckRow` e "Corrigir selecionados"; exportações no menu "Mais"; aba "Painel" passa a "Início" (traduções) | `MainWindow.xaml` (Painel), `MainWindow.Painel.cs`, `Idiomas/*.json` | Testes do Core da contagem dos ladrilhos; LocTests; capturas | Ação principal visível sem rolar a 1040 × 720; nota de saúde continua a assinatura |
| **V5 · Seções e janelas** | `ActionRow` em Limpeza, Espaço, Ajustes, Sistema e Configurações; `ToolWindowShell` nas 12 janelas de ferramenta; migração dos `Margin` para os tokens de espaço | todas as `*Window.xaml`, `MainWindow.xaml` | LocTests; abrir cada janela nos dois temas | Esqueleto único; nenhuma margem solta nas telas migradas |
| **V6 · Polimento e registro** | Movimento (167/250 ms), estados vazios com ação, ícones revistos, remoção das sombras soltas, `DESIGN.md` com os tokens e componentes, atualização da `REVISAO-DESIGN.md`, roteiro de testes visuais | `Motion.cs`, `DESIGN.md`, roteiro | Revisão com `apple-design` de novo, com capturas | Avaliação "Excelente" ou "Bom" sem achados altos |

**Estimativa:** 6 levas do tamanho das anteriores; V4 e V5 são as maiores.

---

## 7. Decisões que são suas

| # | Decisão | Opções | Recomendação |
|---|---|---|---|
| V-D1 | Fonte | (a) Segoe UI Variable em tudo · (b) Segoe UI Variable + Google Sans Flex só na nota de saúde · (c) manter Google Sans Flex | **(b)**: nativo no Windows 11 e a nota mantém a marca |
| V-D2 | Mica (fundo translúcido do Windows 11) | (a) sim, com volta ao sólido · (b) só cores sólidas | **(a)**: é o que mais aproxima do PC Manager; sólido no Windows 10 e com transparência desligada |
| V-D3 | Cor de destaque | (a) azul do Windows fixo (`#60CDFF` / `#005FB8`) · (b) seguir a cor de destaque do Windows quando a opção estiver ligada (já existe) | **(b)** com padrão (a) |
| V-D4 | Navegação | (a) trilho compacto com rótulo sob o ícone, como o PC Manager · (b) lateral larga com ícone e texto, como hoje · (c) as duas, a pessoa escolhe | **(c)** com padrão (a) |
| V-D5 | Nome da primeira aba | "Painel" · "Início" | **"Início"** |
| V-D6 | Onde entregar | (a) nesta branch e neste PR · (b) PR novo depois que o PR #4 for revisado | **(b)**: o PR #4 já é enorme; o redesign merece revisão própria |

---

## 8. Riscos

- **Mica no WPF** exige fundo transparente e extensão da moldura (`DwmExtendFrameIntoClientArea`); só funciona a partir do Windows 11 22H2. Mitigação: detectar a versão e o "Efeitos de transparência" do Windows e cair para cor sólida.
- **Regressão visual** em 16 janelas e ~1.200 linhas de XAML. Mitigação: apelidos dos tokens antigos durante a migração, capturas antes/depois no modo demonstração, uma leva por camada.
- **Textos novos** ("Início", rótulos curtos da navegação) precisam caber em inglês e espanhol: o trilho de 80 px corta palavras longas como "Inicialização" e "Configurações"; os rótulos curtos da navegação serão definidos na V3, junto com as traduções (o nome completo continua no título da página e no Narrador).
- **Não é possível ver o resultado neste ambiente** (Linux, sem Windows): as capturas e a conferência visual dependem do job `windows` da CI (captura automática no modo demonstração, se aprovado) ou do seu teste no PC.
