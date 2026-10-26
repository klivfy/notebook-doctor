# Sistema de design do Notebook Doctor

Linguagem do Windows 11 (Fluent), aplicada pelo redesign V1–V6 (`PLANO-VISUAL-FLUENT.md`). Os valores vêm dos recursos de tema públicos do WinUI (`microsoft-ui-xaml`, licença MIT); nada é copiado de outro produto (logotipos, ilustrações, ícones próprios ou textos).

## Onde fica cada coisa

| Arquivo | Conteúdo |
|---|---|
| `Themes/Light.xaml`, `Themes/Dark.xaml` | Cores (pincéis). Os dois temas têm exatamente as mesmas chaves (`ThemeContrastTests`) |
| `Themes/Tokens.xaml` | Fontes, tamanhos de texto, raios, espaços, anéis de foco |
| `Themes/Controls.xaml` | Controles do WPF reestilizados: botões, caixas, campos, combo, rolagem, tabelas, menus, barra de progresso |
| `Themes/Components.xaml` | Componentes do app: tipografia, cartão, ladrilho, cartão-linha, navegação, camada de conteúdo |
| `ThemeManager.cs` | Troca de tema, Alto Contraste, cor de destaque do Windows, fundo Mica |
| `TextScale.cs` | Multiplica os tamanhos pelo "Tamanho do texto" do Windows |
| `Motion.cs` | Tempos e curva das animações (desligadas com "Efeitos de animação" desligado) |
| `Smoke.cs` | Verificação visual da CI: abre todas as telas nos dois temas e salva imagens |

Cada dicionário de `Controls` e `Components` inclui `Tokens.xaml`, porque o `StaticResource` só enxerga o próprio dicionário e os que ele inclui. Cores sempre por `DynamicResource` (a troca de tema vale na hora).

## Cor

Três planos: **moldura** (`WindowBackdrop`, Mica quando o Windows permite) → **camada de conteúdo** (`LayerFill`, canto superior esquerdo de 8 px) → **cartão** (`CardFill` com contorno `CardStroke`). Sem sombras, exceto em menus, no aviso flutuante e no mini-widget, que ficam por cima de outras coisas.

| Papel | Chaves | Regra |
|---|---|---|
| Texto | `TextPrimary`, `TextSecondary` | 4,5:1 sobre moldura, camada e cartão |
| Ação e seleção | `Accent` / `OnAccent` | Só a ação principal de cada área é preenchida; indicador da navegação |
| Valor clicável | `AccentText` | Números e links que levam a outro lugar |
| Controles neutros | `ControlFill`, `ControlFillHover`, `ControlStroke`, `SecondaryContainer` | Botão padrão, campos, combo, item selecionado |
| Estados | `Critical`/`Caution`/`Success` + `On…` + `…Container` | Cor só para estado; sempre com texto ou ícone junto (a cor nunca é o único sinal) |
| Gráficos | `ChartCpu`, `ChartRam`, `ChartDisk`, `Level*` | Nunca a cor de ação nem as de estado nos gráficos; 3:1 sobre o cartão |

Os nomes descrevem o papel, não a aparência, e seguem o Fluent: `WindowBackdrop`, `LayerFill` e `CardFill` são as camadas (fundo, camada, cartão), `CardStroke` é o contorno do cartão, `TextPrimary` e `TextSecondary` são os textos, `Accent` é a cor de ação, `Caution`, `Critical` e `Success` são os estados, `On…` é o texto sobre a cor (`OnAccent`, `OnCritical`…) e `…Container` é o fundo suave de um estado ou destaque (`CautionContainer`). Até a R4 (outubro de 2026) a cor de fundo se chamava `Surface`, o texto `OnSurface`, a ação `Primary`, e os estados `Error`/`Warning`; o `ResourceKeysTests` falha se uma tela voltar a usar um desses nomes ou uma chave que nenhum tema declara.

Com "Usar a cor de destaque do Windows", `Accent`, `AccentText` e os contêineres de destaque vêm do Windows, ajustados até 4,5:1. No Alto Contraste, todas as chaves viram cores do sistema.

## Texto

Segoe UI Variable (no Windows 10, Segoe UI). Google Sans Flex só na nota de saúde (`FontDisplay`/`FontBrand`).

| Estilo / chave | Tamanho | Uso |
|---|---|---|
| `FsCaption` | 12 | Rótulos curtos da navegação, rodapé |
| `Label` / `FsLabel` | 13 | Rótulos e descrições pequenas |
| `FsBody` | 14 | Texto |
| `CardTitle` / `FsCardTitle` | 16 semibold | Título de cartão e de ladrilho |
| `FsTitle` | 20 semibold | Títulos de diálogo, números das métricas |
| `PageTitle` / `FsPageTitle` | 28 semibold | Título de página e de janela de ferramenta |
| `FsHero` | 56 | Só a nota de saúde |
| `FsIcon` / `FsIconLarge` | 16 / 20 | Ícones (Segoe Fluent Icons) em linhas e botões / na navegação e nos cartões. Crescem com o "Tamanho do texto" |

## Forma e espaço

- Raios: `RadiusControl` 4 (botões, campos, caixas, linhas de tabela), `RadiusCard` 8 (cartões, menus, camada), `RadiusPill` (chips de estado).
- Espaço em múltiplos de 4: `Space4`…`Space32`, `CardPad` 16 (12 no modo compacto), `CardGap` 4 (2 no compacto). Nas telas (os `*.xaml` de `src/NotebookDoctor`), toda margem e preenchimento fica na grade de 4; o `SpacingTests` falha se não ficar. O miolo dos controles em `Themes/` segue as medidas do WinUI (botão 11,5, campo 10,5), e 1 ou valores negativos são ajustes de traço.
- Os componentes de `Themes/Components.xaml` também ficam na grade (`SpacingTests`): só 0 a 2 valem como meio passo. O miolo dos controles em `Controls.xaml` segue o WinUI.
- Início em duas colunas: com a janela a partir de 1200 px (`Density.PainelSideColumn`; o limite sobe com o "Tamanho do texto": ×1,2 a 150%), a nota de saúde, as métricas e os atalhos ficam na coluna da esquerda (5/8) e o Diagnóstico, com os botões Corrigir, na coluna da direita (3/8). É o mesmo cartão trocado de lugar (`ApplyPainelLayout`), com uma rolagem só; abaixo do limite volta à coluna única.
- Alvos de clique: botões com 32 px de altura mínima (40 nas ações principais), linhas de navegação de 40 px (56 no trilho).

## Componentes

| Componente | Estilo | Quando usar |
|---|---|---|
| Botão de destaque | `Button` (padrão) | A ação principal da área. Uma por área |
| Botão padrão | `TonalButton` | Ações secundárias |
| Botão sutil | `TextButton` | Ações terciárias, links |
| Perigo | `DangerButton` | Ação destrutiva, sempre com confirmação |
| Ícone | `IconButton` | Fechar, "Mais"; precisa de `AutomationProperties.Name` |
| Chip de estado | `StatusChipButton` | Um estado que vale para o app todo (Simulação ligada); clicar leva para onde se muda. Nunca para ações |
| Aviso em linha | `InfoBar`, `InfoBarWarning`, `InfoBarError`, `InfoBarSuccess` (Border) | Faixa dentro da tela, que fica enquanto o assunto vale (programa pesado, ponto de restauração). Texto sempre diz o estado; a cor só reforça. O aviso que some sozinho é o `Snack` da janela principal |
| Cartão | `Card` | Agrupar conteúdo |
| Ladrilho | `TileButton` | Atalho com resumo (Painel), cartão inteiro clicável |
| Efeito de clique | `local:ClickRipple.Enabled` | B05: brilho tonal na cor do texto do botão, nascendo no clique/toque e indo até o canto mais distante em 250 ms (`ClickEffect`, tempos do Windows 11). O estilo base de `Button` liga o efeito e o modelo traz o `Border` chamado `Ripple` (padrão, `StatusChipButton` e `IconButton`); sem animações do Windows ou em Alto Contraste (`Motion.Enabled`) só muda o estado "pressionado" |
| Estrela de nota | `StarRadio` | B25: `RadioButton` de 40 px com ícone de estrela; o leitor de tela diz "1 estrela, botão de opção" |
| Cartão-linha | `ActionRow` | Abrir uma ferramenta, outra tela ou um link: ícone de 20 no `Tag`, título (16 semibold) no `Content`, descrição (14, texto secundário) no `ToolTip`. `local:ActionRow.Slot` escolhe o sinal da direita: `Detail` (›, subjanela ou outra seção, padrão) ou `External` (↗, navegador ou programa do Windows). `local:ActionRow.Target` leva o destino (nome do texto embutido, endereço do site), porque o `Tag` é o ícone. A linha inteira é o botão |
| Cartão-linha com botão | `SettingRow` (`HeaderedContentControl`) | Uma ação principal com botão de destaque à direita (ex.: Atualização › Procurar agora). Ícone no `Tag`, título no `Header`, descrição no `ToolTip`, botão no `Content`. Só um por área usa o botão de destaque |
| Título de grupo | `GroupTitle` | Acima de uma lista de cartões-linha (nível 2 para o leitor de tela) |
| Navegação | `NavList` / `NavItem` | Tag da lista: `rail` (trilho, padrão), vazio (larga) ou `compact` (só ícones). `NavLabel.Short` = rótulo do trilho; `NavLabel.Badge` = ponto de pendência |
| Camada de conteúdo | `ContentLayer` | O fundo das páginas da janela principal |

### Estilos de apoio

| Estilo | Para quê |
|---|---|
| `Label` | Rótulo ou descrição pequena (`FsLabel`, cor de texto secundário) |
| `Muted` | Texto de apoio na cor secundária, no tamanho do texto ao redor |
| `Value` | Número ou valor em destaque dentro de um cartão (`FsCardTitle`, média) |
| `EmptyState` | Texto no meio de uma tabela sem linhas. `Tag` aponta para a tabela; some quando ela tem itens |
| `GridCheck` | Caixa de seleção centralizada numa coluna de tabela |
| `WhyToggle` | Link "Por que isso importa?" que abre e fecha a explicação logo abaixo de um achado |
| `RowMin` | Altura mínima de linha de tabela (40; 32 no modo compacto) |

Não há estilo de interruptor: as opções do app são caixas de seleção. Se um dia uma opção precisar de liga/desliga, o estilo entra junto com ela.

## Decisões visuais (fechadas em 26/10/2026)

| # | Decisão |
|---|---|
| V-D1 | Segoe UI Variable em todo o texto; Google Sans Flex com tamanhos óticos só na nota de saúde e nos números grandes |
| V-D2 | Mica no Windows 11 22H2 ou mais novo, com "Efeitos de transparência" ligado e fora do Alto Contraste; cor sólida nos outros casos |
| V-D3 | Azul do Windows (`#60CDFF` no escuro, `#005FB8` no claro) por padrão; "Usar a cor de destaque do Windows" herda a cor do sistema, ajustada até 4,5:1 |
| V-D4 | Trilho compacto (88 px, ícone sobre rótulo curto) por padrão; Configurações › Menu de seções troca para o menu largo (ícone e nome) |
| V-D5 | A primeira seção se chama "Início" (inglês "Home", espanhol "Inicio"). No espanhol, a seção Inicialização passou a se chamar "Arranque" para não repetir o nome. `--tab=painel` e `--tab=inicio` abrem a mesma seção |
| V-D6 | Mudanças visuais e estruturais em PR próprio, depois do klivfy/notebook-doctor#4 |

## Estado para o leitor de tela

- **Nota de saúde:** o número (`ScoreText`) tem o nome "Nota de saúde N de 100, estado". Ele não é lido a cada atualização; o leitor de tela só é avisado quando o estado muda (Bom, Regular, Ruim…).
- **Chip do cabeçalho:** o nome é "Estado do computador: …" e o texto é uma região viva (`LiveSetting="Polite"`), lida quando muda.
- **Ponto de pendência da navegação:** o ponto é só visual; o item também diz "Tem algo para ver" (`ItemStatus`).
- **Gráficos:** `SparklinePeer` com resumo (agora, média, pico).
- Nenhum estado depende só de cor: o chip e o aviso sempre trazem texto.

## Movimento

`Motion.Fast` (167 ms) e `Motion.Normal` (250 ms), curva que desacelera no fim. Nada essencial depende de animação; com "Efeitos de animação" desligado ou Alto Contraste, tudo muda na hora.

## Como conferir uma mudança visual

1. `dotnet test` roda `ThemeContrastTests` (contrastes dos dois temas) e `LocTests` (textos traduzidos).
2. A CI do Windows roda `NotebookDoctor.exe --demo --smoke=pasta`: abre as 11 abas, as janelas de ferramenta, uma folha de controles e os diálogos nos temas claro e escuro, mais as 11 abas num monitor largo (1360 × 800, `-largo`, que confere a troca do Diagnóstico de coluna) e as abas e ferramentas num notebook pequeno (911 × 480) com o texto em 100%, 150% e 200% (`-150`, `-texto150`, `-texto200`), falha se algo não abrir e publica as imagens no artefato **telas**. O relatório `smoke.txt` lista avisos de ligação quebrada e textos que ficaram sem tradução.
