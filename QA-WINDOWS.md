# Teste no Windows real (Prioridade 6)

A CI abre o app num Windows de verdade (job `windows`), mas sem pessoa, sem leitor de tela e sem tela de toque. Este roteiro cobre o que ela
não consegue. Cada item diz o que fazer, o que esperar e onde anotar. Anote o resultado no fim do arquivo (ou numa issue) com a data, a
versão do Windows (`winver`) e o tipo de máquina.

## O que a CI já confere sozinha (não repetir à mão)

O artefato **telas** traz, além das imagens e do `smoke.txt`, o `windows-real.txt`:

- árvore de acessibilidade: cada cartão-linha é um botão só, com nome, descrição, padrão de acionar e foco de teclado;
- as cinco estrelas da avaliação são botões de opção e a escolhida aparece como selecionada;
- efeito de clique: aparece e some em 250 ms com as animações ligadas, e não aparece com elas desligadas;
- WinGet: se a linha de comando responde, qual motor atendeu (COM ou linha de comando), quanto demorou e, se o COM falhou, o código do erro.

Leia o `windows-real.txt` antes de testar à mão: ele diz se o COM do WinGet chegou a funcionar na máquina da CI.

## Preparação

1. Instale a versão da CI (artefato de publicação) ou rode `NotebookDoctor.exe` do build. Use uma conta de usuário comum e, nos itens marcados
   com **[admin]**, repita como administrador.
2. Anote: versão do Windows (10 ou 11), notebook ou desktop, escala de tela, idioma.
3. Para os itens com dados de verdade, **não** use `--demo`.

## 1. WinGet pelo COM (Programas › Atualizar programas…)

| # | Passo | Esperado |
|---|-------|----------|
| 1.1 | Windows 11 com o Instalador de Aplicativo atualizado: abrir a janela, aceitar os termos da fonte e clicar em consultar. | Lista os programas com versão nova; mesmos nomes e versões que `winget upgrade` no terminal. |
| 1.2 | Clicar em "Ver termos…" num programa. | Mostra editor, licença e termos do catálogo. Nada é aceito sozinho. |
| 1.3 | Atualizar um programa pequeno. | O aviso de termos do app aparece antes; o instalador roda pela linha de comando; o resultado vai para Ações. |
| 1.4 | Windows 10 (ou máquina sem o Instalador de Aplicativo): consultar. | Se o WinGet não existe, o app oferece abrir a Microsoft Store. Se existe sem COM, a consulta funciona pela linha de comando, sem mensagem de erro. |
| 1.5 **[admin]** | Rodar o app como administrador e consultar. | Funciona (COM ou, se o COM recusar processo elevado, a reserva), sem erro na tela. |
| 1.6 | Desconectar a rede e consultar. | Mensagem clara ("O WinGet não respondeu" ou parecida), sem travar. |

## 2. Narrador (Win+Ctrl+Enter) e teclado

| # | Passo | Esperado |
|---|-------|----------|
| 2.1 | Navegar com Tab pelos cartões de Ajustes, Sistema, Espaço e Configurações. | Cada cartão-linha é lido uma vez, como botão: título, descrição, "botão". Sem ler o ícone nem a seta. |
| 2.2 | Enter e Espaço num cartão-linha. | Os dois acionam. O anel de foco aparece só com o teclado, não ao clicar com o mouse. |
| 2.3 | Configurações › Avaliar: setas entre as estrelas. | O Narrador diz "1 estrela, botão de opção, não selecionado" e "4 estrelas, selecionado" após escolher. |
| 2.4 | Mudar o status do Início (nota de saúde) e o resultado de uma limpeza. | O Narrador anuncia o novo estado sem mover o foco. |
| 2.5 | Início em monitor largo (≥ 1200 px): Tab do cartão de métricas até o Diagnóstico. | A ordem é a da leitura: esquerda (nota, atalhos, métricas) e depois o Diagnóstico à direita. Corrigir é anunciado com o nome do problema. |
| 2.6 | Repetir 2.1 e 2.3 com o NVDA, se tiver. | Mesmo comportamento. |

## 3. Efeito de clique e Alto Contraste

| # | Passo | Esperado |
|---|-------|----------|
| 3.1 | Clicar em botões padrão, de ícone e no chip "Simulação ligada". | Um brilho suave nasce no ponto do clique e some em cerca de 250 ms; acompanha os cantos arredondados. |
| 3.2 | Clicar com o dedo (tela de toque) ou caneta. | Mesmo efeito, a partir do toque. |
| 3.3 | Windows › Acessibilidade › Efeitos visuais › Efeitos de animação: desligar. Clicar. | Nenhum brilho; só o estado "pressionado". |
| 3.4 | Ligar um tema de Alto Contraste (Alt esquerdo + Shift esquerdo + Print Screen). Percorrer todas as abas. | Texto legível, bordas visíveis, nenhum brilho, foco visível em tudo. O app troca de tema sozinho e volta ao desligar. |

## 4. Telas e tamanhos reais

| # | Passo | Esperado |
|---|-------|----------|
| 4.1 | Escalas de 100%, 125%, 150% e 200% (Configurações do Windows › Vídeo). | Nada cortado, nenhuma rolagem horizontal, lista de resultados com altura útil. |
| 4.2 | "Tamanho do texto" do Windows em 150% e 200%. | Idem; o Início só passa a duas colunas em janela mais larga. |
| 4.3 | Dois monitores com escalas diferentes: arrastar a janela de um para o outro. | Re-escala sem borrar nem quebrar o layout. |
| 4.4 | Maximizar e restaurar; fechar e reabrir. | A janela volta onde estava. |

## 5. Avaliação e estatísticas (sem o servidor da Klivfy ainda)

| # | Passo | Esperado |
|---|-------|----------|
| 5.1 | Configurações › Avaliar: escolher 4 estrelas, escrever um comentário com um e-mail e um caminho de arquivo. | Ao sair da caixa, e-mail e caminho viram `<email>` e `<caminho>`. Reabrir o app: nota e comentário continuam. |
| 5.2 | "Enviar avaliação". | Mostra o texto exato (nota, versão, idioma, comentário) e pede confirmação. Cancelar não envia. Confirmar com a rede ligada, enquanto o receptor não existe: mensagem de que a Klivfy não aceitou e a avaliação continua guardada. |
| 5.3 | Configurações › Privacidade: ligar as estatísticas, usar o app, "Ver o que seria enviado". | Só contagens por ferramenta e falhas, versão, idioma, "10" ou "11" e a semana. Nada de nomes, caminhos, IP ou MAC. |
| 5.4 | Desligar as estatísticas. | A contagem some; "Ver o que seria enviado" mostra tudo zerado. |
| 5.5 | Com um proxy de depuração (ou o Monitor de Rede), conferir o tráfego do app com tudo desligado. | Nenhuma conexão além da consulta diária de versão (se ligada). |

## 6. Itens que continuam do trabalho anterior (Prioridade 3)

Limpeza e remoção em máquina real (com ponto de restauração e Lixeira), drivers do fabricante (Dell, HP, Lenovo), Verificar malware
(Defender e MRT) e atualização assinada do app. Siga as seções "Como conferir" de `MELHORIAS.md` para cada um.

## Resultados

| Data | Windows | Máquina | Itens com problema | Quem |
|------|---------|---------|--------------------|------|
|      |         |         |                    |      |
