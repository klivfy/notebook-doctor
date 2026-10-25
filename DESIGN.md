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

Três planos: **moldura** (`Surface`, Mica quando o Windows permite) → **camada de conteúdo** (`LayerFill`, canto superior esquerdo de 8 px) → **cartão** (`SurfaceContainer` com contorno `CardBorder`). Sem sombras, exceto em menus, no aviso flutuante e no mini-widget, que ficam por cima de outras coisas.

| Papel | Chaves | Regra |
|---|---|---|
| Texto | `OnSurface`, `OnSurfaceVariant` | 4,5:1 sobre moldura, camada e cartão |
| Ação e seleção | `Primary` / `OnPrimary` | Só a ação principal de cada área é preenchida; indicador da navegação |
| Valor clicável | `AccentText` | Números e links que levam a outro lugar |
| Controles neutros | `ControlFill`, `ControlFillHover`, `ControlStroke`, `SecondaryContainer` | Botão padrão, campos, combo, item selecionado |
| Estados | `Error`/`Warning`/`Success` + `On…` + `…Container` | Cor só para estado; sempre com texto ou ícone junto (a cor nunca é o único sinal) |
| Gráficos | `ChartCpu`, `ChartRam`, `ChartDisk`, `Level*` | Nunca a cor de ação nem as de estado nos gráficos; 3:1 sobre o cartão |

Os nomes das cores vêm do primeiro sistema do app e descrevem o papel, não a aparência: `Surface…` são as camadas do Windows 11 (fundo, camada, cartão), `On…` é o texto sobre elas e `…Container` é o fundo suave de um estado ou destaque. As chaves novas usam nomes do Fluent (`ControlFill`, `LayerFill`, `ControlStroke`); renomear as antigas fica para quando o app for para `src/` (R4), porque mexe em todas as telas.

Com "Usar a cor de destaque do Windows", `Primary`, `AccentText` e os contêineres de destaque vêm do Windows, ajustados até 4,5:1. No Alto Contraste, todas as chaves viram cores do sistema.

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
- Espaço em múltiplos de 4: `Space4`…`Space32`, `CardPad` 16 (12 no modo compacto), `CardGap` 4 (2 no compacto). Nas telas (os `*.xaml` da raiz), toda margem e preenchimento fica na grade de 4; o `SpacingTests` falha se não ficar. O miolo dos controles em `Themes/` segue as medidas do WinUI (botão 11,5, campo 10,5), e 1 ou valores negativos são ajustes de traço.
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
| Interruptor | `ToggleSwitch` (CheckBox) | Opção que vale na hora |
| Cartão | `Card` | Agrupar conteúdo |
| Ladrilho | `TileButton` | Atalho com resumo (Painel), cartão inteiro clicável |
| Cartão-linha | `ActionRow` | Abrir uma ferramenta ou outra tela: ícone no `Tag`, título no `Content`, descrição no `ToolTip` |
| Título de grupo | `GroupTitle` | Acima de uma lista de cartões-linha |
| Navegação | `NavList` / `NavItem` | Tag da lista: `rail` (trilho, padrão), vazio (larga) ou `compact` (só ícones). `NavLabel.Short` = rótulo do trilho; `NavLabel.Badge` = ponto de pendência |
| Camada de conteúdo | `ContentLayer` | O fundo das páginas da janela principal |

## Movimento

`Motion.Fast` (167 ms) e `Motion.Normal` (250 ms), curva que desacelera no fim. Nada essencial depende de animação; com "Efeitos de animação" desligado ou Alto Contraste, tudo muda na hora.

## Como conferir uma mudança visual

1. `dotnet test` roda `ThemeContrastTests` (contrastes dos dois temas) e `LocTests` (textos traduzidos).
2. A CI do Windows roda `NotebookDoctor.exe --demo --smoke=pasta`: abre as 11 abas, as janelas de ferramenta, uma folha de controles e os diálogos nos temas claro e escuro, falha se algo não abrir e publica as imagens no artefato **telas**. O relatório `smoke.txt` lista avisos de ligação quebrada e textos que ficaram sem tradução.
