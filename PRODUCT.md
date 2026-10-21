# Product

<!-- impeccable:product-schema 1 -->

## Platform

desktop (Windows 10/11, WPF/.NET 8). Fora dos valores web/ios/android/adaptive do esquema: nenhuma referência nativa de iOS ou Android se aplica.

## Users

Público misto, confirmado com o responsável pelo produto: pessoas comuns com um notebook lento, sem conhecimento técnico, e também técnicos (suporte, TI de pequenas empresas) que querem ver os números por trás do diagnóstico. Situação típica: o computador está lento agora e a pessoa quer saber o motivo e resolver sem risco de estragar nada. O app fica na bandeja do Windows e avisa quando algo muda.

## Product Purpose

Mostrar, em português simples, o que deixa o notebook lento e corrigir com segurança. Sucesso é a pessoa entender o gargalo em segundos (CPU, RAM ou disco), resolver com um clique e poder desfazer. Cobre diagnóstico em tempo real, nota de saúde de 0 a 100, limpeza, processos, inicialização, mapa de espaço, histórico, ajustes do Windows e notificações.

## Positioning

Dois diferenciais, confirmados: (1) segurança e linguagem simples: nada é apagado ou encerrado sem confirmação, há modo simulação, ponto de restauração e "Desfazer" no registro de ações, e cada alerta explica "por que isso importa"; (2) diagnóstico profundo: histórico, nota de saúde com evolução, linha do tempo por programa, SMART, bateria, throttling e vazamento de memória. Um concorrente de "limpeza" não faz o segundo; o Gerenciador de Tarefas não faz o primeiro.

## Operating Context

Janela de desktop (padrão 1160×740, mínimo 720×560), ícone na bandeja, mini-widget opcional, atalho global Ctrl+Alt+N, notificações nativas do Windows com botões. Roda sem administrador por padrão; ações que exigem administrador ficam travadas com explicação. Dados ficam em `%LOCALAPPDATA%\NotebookDoctor` e em chaves do `HKCU`. Interface em português do Brasil, com números no formato pt-BR.

## Capabilities and Constraints

- .NET 8, WPF, WinForms só para o ícone da bandeja. Fonte Google Sans Flex embutida (SIL OFL).
- Tema claro, escuro e Alto Contraste; segue o "Tamanho do texto" do Windows (100% a 225%).
- Atualização própria: manifesto em klivfy.com, SHA-256 e assinatura Authenticode da Klivfy.
- Decisão em aberto: a distribuição pode incluir a Microsoft Store além do instalador da Klivfy. Pacote MSIX da Store não pode usar o atualizador próprio nem escrever livremente em `HKCU` e no registro de inicialização; isso precisa ser decidido antes de empacotar.
- Itens do `MELHORIAS.md` marcados como "não testado" não devem ser descritos como funcionando.

## Brand Commitments

Marca Klivfy Tecnologia e Consultoria em TI LTDA. Suporte em klivfy.com/suporte. Voz: direta, em português simples, sem jargão sem explicação ao lado (TRIM, SFC, vmmem aparecem só com contexto). Verbos específicos nos botões ("Encerrar", "Apagar"), nunca "OK" em confirmações.

## Evidence on Hand

Não há depoimentos, métricas de uso nem número de clientes; nada disso deve ser inventado. Existem: `MELHORIAS.md` (150 melhorias e status), `REVISAO-DESIGN.md` (revisão de design e acessibilidade, com contrastes medidos), 59 testes automatizados e capturas de tela geradas pelo CI (artefato `telas-do-app`).

## Product Principles

1. Segurança antes de potência: toda ação que altera o sistema tem confirmação, simulação e Desfazer.
2. Dizer o que está errado em uma frase antes de mostrar números; os números ficam a um clique para quem quer.
3. Cor só para estado; uma cor de ação. Nada decorativo compete com a nota de saúde.
4. Acessível por padrão: teclado, leitor de tela, Alto Contraste e texto ampliado são requisitos, não extras.
5. Honestidade sobre o que foi testado: o produto não promete o que não verificou.

## Accessibility & Inclusion

Navegação completa por teclado com foco visível, UI Automation com nomes e resumo dos gráficos, escala de texto do Windows, Alto Contraste. Contrastes mínimos: 4,5:1 para texto e 3:1 para contornos e gráficos. Pendente de verificação em máquina real: Narrador, Alto Contraste e texto a 200% (ver plano de melhorias, Fase 1).
