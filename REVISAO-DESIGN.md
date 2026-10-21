# Revisão de design: Notebook Doctor 1.1 (WPF/.NET 8, Windows 11)

Feita com a skill `apple-design`. O app é Windows, então valem os **princípios e fundamentos** da HIG (acessibilidade, cor, tipografia, layout, escrita, alertas); as convenções específicas de iOS/macOS (barra de menus, janela de Ajustes) **não se aplicam** e foram traduzidas para os equivalentes do Windows.
Material analisado: `MainWindow.xaml`, `Themes/Styles.xaml`, `Themes/Light.xaml`, `Themes/Dark.xaml`, `Dialogs.cs`, `Sparkline.cs` e as capturas de tela feitas durante os lotes 1–5. Contrastes calculados pela fórmula WCAG a partir dos hex do tema (não estimados de imagem).

## Resumo

**Avaliação: Problemas críticos.** A base é sólida — tokens de cor por função, dois temas completos, barra de título no tom do tema, confirmações com verbos específicos e "Desfazer". Mas três falhas de acessibilidade travam o app para quem usa teclado, leitor de tela ou texto ampliado.
**Tese da tela principal:** "diga em segundos o que deixa o notebook lento e resolva com segurança". O elemento que poderia ser lembrado é a **nota de saúde com o pulso do ícone**, mas hoje ela é coadjuvante: a tela gasta o destaque em três números gigantes iguais (CPU/RAM/Disco) e em muitas cores ao mesmo tempo.

## Crítico

**1. Foco do teclado invisível.**
- **O quê:** `FocusVisualStyle={x:Null}` em botões, caixas de seleção, abas, combos, linhas e células de tabela (`Themes/Styles.xaml:50, 86, 127, 177, 271, 344, 357, 392`). O botão só ganha uma camada de 14% de opacidade (`Styles.xaml:63`); abas, checkboxes e linhas não mostram nada. Quem navega com Tab não sabe onde está.
- **Por quê:** `accessibility.md › Speech`: "Let people use the keyboard alone to navigate and interact with your app."
- **Correção:** um anel de foco único: `Border` de 2 px na cor `Primary` com 2 px de afastamento, visível só com `IsKeyboardFocused` (o WPF já distingue foco de teclado de clique). Aplicar em Button, TabItem, CheckBox, ComboBox, TextBox e DataGridRow.

**2. Conteúdo das abas invisível para leitores de tela.**
- **O quê:** no teste com UI Automation (lote 2), a árvore de acessibilidade continha só as 8 abas e o cabeçalho; botões, tabelas e textos das abas não apareciam. O Narrador não lê o diagnóstico nem encontra "Otimizar agora". Os gráficos (`Sparkline`) não têm nome nem valor acessível.
- **Por quê:** `accessibility.md › Vision`: "Describe your app's interface and content for VoiceOver." `charting-data.md`: "Make every chart in your app accessible."
- **Correção:** o `TabControl` customizado precisa manter o `ContentPresenter` com `x:Name="PART_SelectedContentHost"` (é por ele que o `TabControl` expõe o conteúdo à automação); dar `AutomationProperties.Name` aos cartões, ao chip de status e a cada `Sparkline` (ex.: "CPU, últimos 2 minutos, média 18%, pico 41%"); criar um `AutomationPeer` simples para `Sparkline`.

**3. Texto não acompanha o tamanho de texto do Windows.**
- **O quê:** todos os tamanhos são fixos (14, 16, 22, 40 px). Em Configurações › Acessibilidade › Tamanho do texto, o app não muda. Rótulos de 12 px (`Styles.xaml:28`) = 9 pt, abaixo do mínimo de 10 pt para desktop.
- **Por quê:** `accessibility.md › Vision`: "Support larger text sizes… give people the option to enlarge text by at least 200 percent." Tabela de tamanhos: macOS padrão 13 pt, mínimo 10 pt.
- **Correção:** ler `Windows.UI.ViewManagement.UISettings.TextScaleFactor` (ou o valor `TextScaleFactor` em `HKCU\Software\Microsoft\Accessibility`) e multiplicar os tamanhos do tema (definir os tamanhos como recursos `Double` e reaplicar quando mudar); o layout dos cartões precisa então rolar em vez de cortar. Subir `Label` para 13 px.

## Melhorias

**4. [Alta] Barras de nível invisíveis no tema claro.** Lima `#BAD80A` sobre o cartão `#F2F2F9`: **1,46:1**; amarelo `#FFF100`: **1,06:1** (objetos gráficos pedem 3:1). No escuro passam (10,3:1 e 14,2:1). `color.md › Inclusive color`: "insufficient contrast can cause icons and text to blend with the background". **Correção:** no claro, usar a escala `#009E49` (3,15:1) → `#FF8C00` com contorno de 1 px `#7A7A82` (3,82:1) → `#E81123` (4,15:1); manter lima/amarelo só no escuro. `LevelBrush` em `MainWindow.xaml.cs:527` deve pedir a cor ao tema em vez de usar hex fixo.

**5. [Alta] Contornos de campos e trilhas fracos no claro.** `Outline = #66555555` (`Light.xaml:10`) ≈ `#ABABB1`: **2,05:1** no cartão. Bordas de TextBox/ComboBox e trilhas de progresso ficam quase invisíveis. **Correção:** `Outline` claro `#7A7A82` (3,82:1); no escuro o atual (≈3,52:1) passa.

**6. [Alta] O mais importante fica no pé da janela.** Em 1040×720, o Diagnóstico — onde estão os botões "Corrigir" e "Otimizar agora" — começa abaixo da metade e mostra 1 ou 2 achados. Os três cartões de métricas, que repetem o que a nota já resume, ocupam o topo. `layout.md`: "Order content by relative importance… place the most important items near the top." e "Avoid placing controls or critical information at the bottom of a window." **Correção:** topo = nota de saúde + gargalo + ação principal; métricas em uma faixa compacta (número, barra e gráfico numa linha de ~72 px); Diagnóstico ocupa o resto. Em larguras ≥ 1200 px, diagnóstico numa coluna à direita.

**7. [Alta] Oito abas no topo.** Painel, Processos, Limpeza, Espaço, Inicialização, Histórico, Ações e Configurações já preenchem a largura e não têm para onde crescer. `layout.md`: "Consider taking advantage of larger spaces to switch from a tab bar to a sidebar." (Em Windows: `NavigationView` à esquerda.) **Correção:** barra lateral com ícone + rótulo, Configurações fixa no rodapé da barra (padrão do Windows 11), recolhível em janelas estreitas.

**8. [Média] Alertas usados para informar.** Diálogos modais com OK depois de Otimizar, Modo Foco, Perfil e WSL (`MainWindow.xaml.cs:75, 122, 136, 143, 390`). `alerts.md › Best practices`: "Avoid using an alert merely to provide information." **Correção:** um snackbar/InfoBar no rodapé do conteúdo (some sozinho ou ao clicar, com "Desfazer" quando houver) e o detalhe no registro de Ações. Manter modal só para confirmações destrutivas — essas estão certas.

**9. [Média] Mesma cor, dois sentidos.** Em ambos os temas, a cor da CPU no gráfico é a mesma cor de ação (abas selecionadas, botões, links): `#00188F` no claro e `#00BCF2` no escuro. `color.md`: "Avoid using the same color to mean different things." **Correção:** CPU em teal `#00B294` no escuro (6,19:1); no claro o teal não chega a 3:1 (2,42:1), então usar `#68217A` para CPU e mover RAM para `#E81123`/`#EC008C` com contorno — ou manter as séries em tons neutros e reservar cor só para o estado (acima do limite).

**10. [Média] Chip "OK" no tema claro.** Branco sobre `#009E49`: **3,51:1** em texto de 12 px. **Correção:** texto preto: **5,99:1** (o tema escuro já usa preto).

**11. [Média] Escrita.** "2 problema(s)" e "processo(s)" → plural real ("1 problema", "2 problemas"). Cabeçalhos com jargão ou abreviação: "Proc." → "Processos"; "E/S KB/s" → "Disco e rede"; "Fechar?" → "Pode fechar?". `writing.md`: "Choose simple, plain language… avoiding jargon". "TRIM", "SFC/DISM" e "vmmem" aparecem em textos do usuário: manter só onde há explicação ao lado.

**12. [Média] Limpeza: item marcado que não pode ser limpo.** "Temporários do Windows (exige administrador)" vem marcado sem administrador, e o total "marcados para limpeza" não atualiza quando se marca ou desmarca. **Correção:** desmarcado e desabilitado sem admin; `CleanItem` com `INotifyPropertyChanged` para o total reagir.

**13. [Baixa] Seletor de tema próprio.** `dark-mode.md`: "Avoid offering an app-specific appearance setting." No Windows é comum oferecer tema por app, e o padrão aqui já é "Seguir o Windows" — manter, mas tirar da tela de boas-vindas (é mais uma decisão no primeiro contato).

**14. [Baixa] Marca repetida.** O ícone de 40 px e "Notebook Doctor" em 22 px no cabeçalho repetem o que a barra de título já mostra. `branding.md`: "Resist the temptation to display your logo throughout your app." **Correção:** usar esse espaço para a nota de saúde (ver nota de craft).

## Notas de craft

- **[Alta] Ponto de vista diluído.** Hoje a tela é Material 3 bem executado, mas genérica: três "número grande sobre rótulo pequeno" iguais, o padrão de dashboard que a skill aponta como template. O que é deste produto e de mais nenhum é a **nota de saúde** e o **pulso** do ícone. Torná-los o elemento-assinatura: nota grande (40 px) no topo, com uma linha de pulso desenhada com o histórico da própria nota (hoje ela só existe no Histórico), e o gargalo em uma frase ao lado. O resto fica quieto.
- **[Alta] Ousadia gasta em todo lugar.** Ao mesmo tempo aparecem ciano, magenta, verde, lima, amarelo, laranja e vermelho. `branding.md`: a marca deve ficar para ações primárias e estados. Regra proposta: **cor só para estado** (verde/laranja/vermelho da paleta) e **uma cor de ação** (`#00188F` claro / `#00BCF2` escuro); gráficos em neutro, ganhando cor só quando passam do limite.
- **[Média] Paleta com história.** Os hex escolhidos são a paleta clássica de acentos do Windows 8 (Metro) mais o verde/preto do Spotify. É uma escolha com raiz no Windows — vale assumir: blocos de cor chapada, cantos menores, tipografia mais firme, em vez do M3 suave por cima.
- **Remover um acessório:** o título "Notebook Doctor" do cabeçalho (a barra de título já diz).

## O que funciona

- Tokens por função (`Surface`, `OnSurface`, `Primary`, `Error`…) com `DynamicResource`: troca de tema sem reiniciar, e a barra de título segue o tema.
- Texto principal com contraste alto nos dois temas: 16,8:1 (claro) e 16,7:1 (escuro); secundário 6,7:1 e 8,4:1.
- Confirmações destrutivas corretas: verbo específico ("Encerrar", "Apagar", "Reiniciar") em vez de OK, botão de perigo vermelho à direita e Cancelar à esquerda — `alerts.md`: "Avoid using OK as the default button title unless the alert is purely informational."
- Segurança como design: Lixeira e caches desmarcados por padrão, nada destrutivo via link `notebookdoctor:`, registro de Ações com Desfazer.
- Estado sempre visível perto do que descreve (chip de status, barra, explicação do gargalo) — `feedback.md`: "Consider integrating status feedback into your interface."
- Números em pt-BR, Google Sans Flex embutida com tamanhos óticos (9 pt para texto, 36 pt para números grandes).

## Notas de plataforma (Windows)

- Equivalentes: barra lateral = `NavigationView`; snackbar = `InfoBar`; leitor de tela = Narrador via UI Automation (`AutomationProperties`, `AutomationPeer`); tamanho de texto = `UISettings.TextScaleFactor`.
- Para parecer nativo do Windows 11, vale aplicar Mica ao fundo da janela (`DWMWA_SYSTEMBACKDROP_TYPE`), só na moldura, nunca atrás do conteúdo.
- Testar com Alto Contraste ligado: o tema próprio ignora `SystemColors`. No mínimo, detectar `SystemParameters.HighContrast` e usar as cores do sistema.

## Ordem sugerida

1. Críticos 1–3 (foco, leitor de tela, tamanho de texto).
2. Contrastes 4, 5, 10 (troca de valores no tema; rápido).
3. Estrutura 6–7 (nota de saúde no topo, barra lateral).
4. Escrita e detalhes 8, 9, 11–14.

## Correções aplicadas (19/10/2026)

| # | Situação | O que mudou |
| --- | --- | --- |
| 1 | Feito | Anéis de foco `FocusRing`, `FocusRingPill` e `FocusRingTight` (2 px na cor `Primary`, só com teclado) em botões, caixas, combos, itens da barra lateral, linhas e células. O campo de texto mantém o próprio contorno de 2 px. Conferido com Tab: o anel aparece em "Otimizar agora". |
| 2 | Feito | `PART_SelectedContentHost` no `TabControl`. Pela UI Automation, o Painel expõe 95 elementos: botões, barras com nome e os gráficos com resumo ("Processador, últimos minutos: agora 22%, média 50%…"). `SparklinePeer` (tipo Imagem), `HeadingLevel` nos títulos, `LiveSetting` nas mensagens de status, `HelpText` nos botões "Corrigir" e um botão "Detalhes" em Processos (antes só havia clique duplo). |
| 3 | Feito | `TextScale.cs` lê `TextScaleFactor` e multiplica os tamanhos `FsLabel`…`FsHero` (usados via `DynamicResource`, inclusive nos diálogos). Rótulo mínimo de 12 → 13 px. O Painel rola, as opções quebram linha, as linhas das tabelas crescem (`MinRowHeight`) e a barra lateral fica compacta. Conferido a 150%. |
| 4 | Feito | Escala de carga no tema (`LevelLow`…`LevelCrit`): no claro verde → laranja → vermelho, no escuro lima → amarelo → laranja → vermelho. A trilha da barra ganhou contorno `Outline`. |
| 5 | Feito | `Outline` claro `#7A7A82` e escuro `#8A8A8A`, mais um `OutlineVariant` suave para divisórias e grades de gráfico. |
| 6 | Feito | Painel: nota de saúde em 56 px com chip de estado e pulso da última hora, o gargalo e as três ações principais no topo, os fatos do hardware logo abaixo, as métricas numa faixa compacta e o diagnóstico em seguida (a página rola). Em telas largas o diagnóstico continua abaixo, sem a coluna lateral à direita. |
| 7 | Feito | Barra lateral (padrão NavigationView) com ícones Segoe Fluent e Configurações no rodapé. Fica só com ícones abaixo de 960 px de largura (proporcional ao tamanho do texto). Ctrl+1…8 continua valendo. |
| 8 | Feito | Aviso flutuante (snackbar) com ação e Esc/X para fechar. Ele substitui os diálogos de Otimizar, Modo Foco, Perfil, WSL e processo protegido. Os diálogos ficaram só para confirmações. |
| 9 | Feito | Gráficos: CPU `#68217A`/`#00B294`, memória `#EC008C`, disco em neutro. Nenhuma série usa a cor de ação. `SecondaryContainer` virou o tom da cor de ação, então o roxo e o magenta saíram dos botões. |
| 10 | Feito | Texto preto sobre o verde no tema claro (`OnSuccess`). |
| 11 | Feito | Plurais reais (`Pt.N`) e colunas "Processos", "Memória (MB)", "Disco e rede (KB/s)" e "Pode fechar?". TRIM, SFC e DISM ganharam explicação ao lado. |
| 12 | Feito | `CleanItem` com `INotifyPropertyChanged` e `CanClean`: os itens que exigem administrador ficam desmarcados e travados, e o total muda a cada caixa marcada ou desmarcada. |
| 13 | Feito | O tema saiu da tela de boas-vindas (segue o Windows; troca em Configurações). |
| 14 | Feito | O logo e o título saíram do cabeçalho, que agora mostra o nome da seção atual. |

Ficaram de fora: a coluna de diagnóstico à direita em telas largas e o Mica (notas de plataforma). O Alto Contraste existe (`ThemeManager.HighContrastTheme`) e foi exercitado no CI com paletas simuladas; veja a seção seguinte.
Também corrigido durante o teste: o texto das seções herdava o preto do sistema pelo `TabItem` e sumia no tema escuro.

## Verificação automatizada (Fase 1, 21/10/2026)

O CI abre o app num Windows real (`NotebookDoctor.exe --screenshots=PASTA`, job `ui-screenshots`), percorre as 9 abas e publica o artefato `telas-do-app` com os PNGs e um relatório (`acessibilidade.txt`). Variantes: normal, texto a 200%, janela de 720 px, e Alto Contraste preto e branco. O monitor virtual do runner limita a janela a 1044 px de largura, não os 1160 px padrão.

| Verificação | Resultado | Situação |
| --- | --- | --- |
| Nome acessível dos controles (o que o Narrador lê) | Nenhum controle sem nome; campos `Cfg*` nomeados por `LabeledBy` | Verificado por máquina. Narrador real **não testado** |
| Alvo de clique mínimo de 24×24 px | Nenhum abaixo disso | Verificado |
| Texto cortado pelo layout | De 31 achados distintos para 3 inofensivos (reticências no limite do pixel, ícone, texto de ajuda) | Corrigido em `c39f7ce` e `d08b06f` |
| Painel a 200% | A coluna "Gargalo agora" chegava a largura 0 (texto quebrava letra por letra). Agora as ações descem para uma linha própria quando faltam pixels | Corrigido |
| Rolagem horizontal em Processos | 0 px a 1044 px; **99 px** a 200% de texto e **181 px** a 720 px de largura | Aberto: tabela precisa de coluna fixa ou colunas flexíveis |
| Alto Contraste | Mapeamento do tema roda sem erros com as paletas clássicas preta e branca (simuladas, não o modo real do Windows) | Falta olhar as capturas `hc-*` e testar com o modo real |
| Contraste de texto | O cálculo automático por fundo de elementos não é confiável (modelos de controle ignoram `Background`); os valores medidos por fórmula acima seguem valendo | Pendente: medir por amostragem de pixels das capturas |

Mudanças feitas nesta rodada: rótulos (`Label`) quebram linha; células de tabela cortam com reticências e mostram o texto inteiro na dica (`CellText`); combos de largura fixa viraram `MinWidth`; colunas numéricas da Limpeza ajustam ao conteúdo; título da seção quebra linha.

Hipótese ainda não confirmada: no tema de Alto Contraste, `Error`, `Warning`, `Success` e a escala `Level*` apontam todos para a cor de destaque do sistema, então o estado só se distingue pelo texto. Conferir nas capturas `hc-preto-00-painel` e `hc-branco-00-painel`.
