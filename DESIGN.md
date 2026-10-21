---
name: Notebook Doctor
description: Monitor de desempenho e limpeza segura para Windows, em Material Design 3 com a paleta clássica de acentos do Windows.
colors:
  primary-light: "#00188F"
  primary-dark: "#00BCF2"
  surface-light: "#FFFFFF"
  surface-container-light: "#F2F2F9"
  surface-variant-light: "#EBEBF6"
  on-surface-light: "#121212"
  on-surface-variant-light: "#555555"
  outline-light: "#7A7A82"
  outline-variant-light: "#D6D6E0"
  surface-dark: "#121212"
  surface-container-dark: "#1E1E1E"
  surface-variant-dark: "#2A2A2A"
  on-surface-dark: "#FFFFFF"
  on-surface-variant-dark: "#B8B8B8"
  outline-dark: "#8A8A8A"
  outline-variant-dark: "#3A3A3A"
  error: "#E81123"
  warning: "#FF8C00"
  success-light: "#009E49"
  success-dark: "#1ED760"
  chart-cpu-light: "#68217A"
  chart-cpu-dark: "#00B294"
  chart-ram: "#EC008C"
  chart-disk-light: "#555555"
  chart-disk-dark: "#B8B8B8"
typography:
  display:
    fontFamily: "Google Sans Flex 36pt SemiBold, Segoe UI"
    fontSize: "40px"
    fontWeight: 600
  hero:
    fontFamily: "Google Sans Flex 36pt SemiBold, Segoe UI"
    fontSize: "56px"
    fontWeight: 600
  title-large:
    fontFamily: "Google Sans Flex 9pt Medium, Segoe UI"
    fontSize: "22px"
    fontWeight: 500
  title-medium:
    fontFamily: "Google Sans Flex 9pt Medium, Segoe UI"
    fontSize: "16px"
    fontWeight: 500
  body:
    fontFamily: "Google Sans Flex 9pt Regular, Segoe UI"
    fontSize: "14px"
    fontWeight: 400
  label:
    fontFamily: "Google Sans Flex 9pt Regular, Segoe UI"
    fontSize: "13px"
    fontWeight: 400
rounded:
  card: "16px"
  button: "20px"
  nav-item: "22px"
spacing:
  card-padding: "20px"
  card-margin: "6px"
  button-padding: "24px 8px"
components:
  card:
    backgroundColor: "{colors.surface-container-light}"
    rounded: "{rounded.card}"
    padding: "{spacing.card-padding}"
  button-primary:
    backgroundColor: "{colors.primary-light}"
    textColor: "{colors.surface-light}"
    rounded: "{rounded.button}"
    height: "40px"
  button-danger:
    backgroundColor: "{colors.error}"
    textColor: "{colors.surface-light}"
    rounded: "{rounded.button}"
    height: "40px"
  nav-item:
    textColor: "{colors.on-surface-variant-light}"
    rounded: "{rounded.nav-item}"
    height: "44px"
---

# Design System: Notebook Doctor

Documento extraído do código (`Themes/Light.xaml`, `Themes/Dark.xaml`, `Themes/Styles.xaml`), não de uma intenção de design. Onde algo é decisão e não fato do código, está marcado como direção.

## Overview

App de tarefa para Windows (modo Operate): a pessoa abre para entender por que o notebook está lento e resolver. A base é Material Design 3 (cartões tonais, botões em pílula, barra lateral estilo NavigationView), com a paleta clássica de acentos do Windows e a fonte Google Sans Flex embutida. O elemento que é só deste produto é a **nota de saúde de 0 a 100 com o pulso da última hora**, no topo do Painel; o resto é M3 bem executado e, por isso, genérico.

Direção (a confirmar): deixar a nota de saúde dominar a hierarquia visual e tratar o resto como apoio.

## Colors

- **Uma cor de ação:** `Primary` (`#00188F` no claro, `#00BCF2` no escuro). Botões, item de navegação selecionado, links e anel de foco.
- **Cor só para estado:** `Success`, `Warning` e `Error` marcam estado e escala de carga. A escala das barras (`LevelLow` a `LevelCrit`) usa verde, laranja e vermelho no claro; lima, amarelo, laranja e vermelho no escuro, porque lima e amarelo somem sobre fundo claro.
- **Gráficos sem cor de ação:** CPU roxo (`#68217A`) no claro e teal (`#00B294`) no escuro, memória magenta (`#EC008C`), disco neutro. Nenhuma série usa `Primary`, para a mesma cor não significar duas coisas.
- **Superfícies:** `Surface` (fundo), `SurfaceContainer` (cartões), `SurfaceVariant`. Texto principal `OnSurface`, secundário `OnSurfaceVariant`.
- **Contornos:** `Outline` (`#7A7A82` claro, `#8A8A8A` escuro) para campos e trilhas, com 3:1 ou mais; `OutlineVariant` para divisórias.
- **Alto Contraste:** o tema próprio sai de cena e valem as cores do sistema (`SystemColors`); cartões ganham `CardBorder`.
- O `OnSuccess` é preto nos dois temas, porque branco sobre `#009E49` ficaria em 3,5:1.

## Typography

Google Sans Flex em três cortes ópticos: 9 pt (texto e rótulos), 36 pt SemiBold (números grandes), com Segoe UI de reserva. Tamanhos são recursos (`FsLabel` 13, `FsBody` 14, `FsTitleMedium` 16, `FsTitle` 20, `FsTitleLarge` 22, `FsDisplay` 40, `FsHero` 56) e são multiplicados pelo "Tamanho do texto" do Windows (100% a 225%). Nunca usar tamanho fixo no XAML. Números seguem o formato pt-BR.

## Layout

Barra lateral à esquerda (200 px, só ícones abaixo de 960 px de largura, com tooltip), conteúdo à direita, aviso flutuante no rodapé. Janela de 1160×740, mínimo 720×560. Painel: nota de saúde e ações principais no topo, fatos do hardware, faixa compacta de métricas e diagnóstico, com rolagem. Cartões com 20 px de espaço interno e 6 px de margem. Ctrl+1 a Ctrl+9 trocam de aba; F5 atualiza.

## Elevation & Depth

Plano. Profundidade vem de camadas tonais (`Surface` sobre `SurfaceContainer`), não de sombra. O aviso flutuante usa `InverseSurface` para destacar. Mica na moldura da janela é uma intenção ainda não aplicada.

## Shapes

Cantos arredondados em escala: cartões 16 px, botões e itens de navegação em pílula (20 e 22 px). Direção em aberto: cantos menores e blocos de cor chapada aproximariam o app da herança Windows da paleta.

## Components

- **Botões:** preenchido (padrão, `Primary`), tonal (`SecondaryContainer`), texto, perigo (`Error`) e só ícone (40×40). Camada de estado: 10% no hover, 18% pressionado, 38% de opacidade desabilitado. Altura mínima de 40 px.
- **Cartão (`Card`):** `SurfaceContainer`, raio 16, sem contorno (contorno só em Alto Contraste).
- **Barra lateral (`NavItem`):** pílula de 44 px, indicador de 3 px à esquerda no item selecionado, fundo `SecondaryContainer`. No modo compacto mostra só o ícone.
- **Anel de foco:** 2 px na cor `Primary`, só com teclado, em botões, abas, caixas, combos, linhas e células.
- **Tabelas (`DataGrid`):** linhas crescem com o tamanho do texto; ícones reais dos programas em Processos.
- **Aviso flutuante:** substitui diálogos informativos; Esc fecha. Diálogos modais ficam só para confirmações, com verbo específico e botão de perigo à direita.
- **Gráfico (`Sparkline`):** expõe resumo textual ("agora 22%, média 50%…") à UI Automation.

## Do's and Don'ts

- Faça: usar tokens (`DynamicResource`) para cor e tamanho de texto; testar nos dois temas e com Alto Contraste.
- Faça: nomear todo controle sem texto com `AutomationProperties.Name`.
- Faça: confirmar com verbo específico e oferecer Desfazer em ações que alteram o sistema.
- Não faça: usar `Primary` como cor de série de gráfico ou de estado.
- Não faça: usar diálogo modal para informar algo; use o aviso flutuante.
- Não faça: tamanhos de fonte fixos ou cores em hexadecimal no XAML das telas.
- Não faça: lima ou amarelo sobre fundo claro (contraste abaixo de 1,5:1).
