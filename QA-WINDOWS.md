# Teste no Windows real (Prioridades 3, 5 e 6; Fases 5 a 12)

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

## 6. Antes de mexer no sistema (regra para todas as seções abaixo)

As seções 7 a 16 testam funções que mudam o Windows de verdade. Use uma máquina de teste (ou uma máquina virtual com instantâneo), nunca a de trabalho.

1. Crie um ponto de restauração do Windows e, se for máquina virtual, um instantâneo.
2. Em cada item, faça primeiro com **Simulação ligada** (o chip "Simulação ligada" fica no topo): o app deve dizer o que faria e não mudar nada.
3. Depois desligue a simulação, execute, confira o resultado e use **Desfazer em Ações**. Confirme que o estado voltou ao que era.
4. A caixa de confirmação sempre tem **Cancelar** como padrão (Enter cancela). Anote qualquer caixa em que não seja assim.
5. Anote no fim do arquivo: o item, o que aconteceu e, se falhou, a tela e o texto exato.

Legenda: **[admin]** exige o app como administrador; **[pendrive]** use um pendrive ou cartão de teste, sem nada seu dentro.

## 7. Limpeza, registro e apagar de vez (Fases 6 e 7)

| # | Passo | Esperado |
|---|-------|----------|
| 7.1 | Limpeza: marcar temporários e analisar; limpar. | Mostra o tamanho a liberar e o que sobrou; arquivos em uso são pulados sem erro. Aparece em Ações com backup. |
| 7.2 | Limpeza › Limpeza avançada…: rodar as regras de rastros do Windows e do Office; importar um arquivo de regras de teste (`winapp2.ini`). | Cada regra mostra o que apaga; regras com caminho protegido (perfil inteiro, System32) são recusadas. |
| 7.3 | Limpeza › Limpeza avançada…: Limpeza de Disco do Windows. | Abre o `cleanmgr` com as categorias escolhidas e volta ao app. |
| 7.4 | Limpeza › Verificar registro…, num Windows com anos de uso. | Seis categorias, só com prova; ProgID vem desmarcado; unidade ausente nunca é apontada. Desfazer devolve cada chave. |
| 7.5 | Verificar registro: limpar um item, abrir o programa dono dele. | O programa abre normal. |
| 7.6 | Apagar de vez…: um arquivo de teste. | Sobrescreve, renomeia e apaga; o arquivo não volta na Lixeira. Recusa perfil inteiro, OneDrive, links e o próprio app. |
| 7.7 | Apagar de vez…: espaço livre de uma unidade de teste. | Barra de progresso em blocos de 256 MB e botão Cancelar que funciona. |
| 7.8 **[pendrive]** | Apagar de vez…: unidade externa inteira. | Só aceita USB e cartão; exige digitar a letra e o nome; formata por completo. O disco do Windows nunca aparece na lista. |
| 7.9 | Inicialização › Menu de contexto…: desativar e reativar um verbo e um manipulador. | O item some do clique direito do Explorador e volta com "Reativar". Itens do Windows não podem ser desativados. |

## 8. Programas, Inicialização e Ajustes (Fases 5, 7 e 11)

| # | Passo | Esperado |
|---|-------|----------|
| 8.1 | Programas: desinstalar um programa de teste pelo desinstalador oficial. | O app espera o desinstalador terminar e depois varre as sobras em 3 níveis, com revisão, backup e desfazer. |
| 8.2 | Programas: apps da Store e atualizações do Windows. | A lista bate com Configurações › Aplicativos e com o Histórico de Atualizações. |
| 8.3 | Inicialização: desativar um item e reativar pelo Desfazer. | O item aparece desativado no Gerenciador de Tarefas e volta ao Desfazer. |
| 8.4 | Inicialização › Atrasar 2 min…, com "abrir com o Windows" ligado. Reiniciar. | O app abre sozinho e o item abre cerca de 2 minutos depois do login. O Desfazer devolve o item ao Windows. |
| 8.5 | Inicialização: colunas "Atrasou o boot" e "Na bateria". | Preenchidas só com dados existentes; sem administrador, a coluna diz que falta permissão em vez de inventar. |
| 8.6 **[admin]** | Ajustes › Serviços: mudar um serviço de terceiros para manual e desfazer. | O serviço muda no `services.msc` e volta. Serviços do Windows, de segurança, drivers e rede nunca aparecem. |
| 8.7 | Ajustes › Tarefas agendadas de terceiros: desativar e desfazer. | A tarefa muda no Agendador de Tarefas e volta. |
| 8.8 **[admin]** | Ajustes › Pontos de restauração: apagar todos menos o último (só com cópia de sombra de teste). | Mantém o mais recente. |

## 9. Processos e destravador de arquivos (Fase 10)

| # | Passo | Esperado |
|---|-------|----------|
| 9.1 | Processos: abrir um programa e travá-lo de propósito (por exemplo, pausar o processo no Gerenciador de Tarefas ou usar uma janela sem resposta). | A linha ganha o selo "Travado" (texto, lido pelo Narrador) e sobe para logo abaixo dos fixados. |
| 9.2 | Filtros rápidos (todos, uso alto, travados, com janela, segundo plano, podem fechar). | Cada filtro mostra só o grupo certo e o rodapé diz quantos itens ficaram escondidos. |
| 9.3 | Clique direito: Fixar no topo (até 5), Ver histórico, Pôr em soneca, Ver conexões de rede. | Cada uma abre a tela certa já no programa escolhido; as conexões mostram endereço, porta e estado. |
| 9.4 | Clique direito: Modo eficiência automático fora de foco. | O programa entra no modo eficiência ao trocar de janela e sai ao voltar. |
| 9.5 | Processos: Reiniciar o Explorador… e Reiniciar o driver de vídeo…. | A barra de tarefas some e volta; a tela pisca. Nada do que estava aberto se perde. |
| 9.6 | Destravar arquivo ou pendrive: abrir um documento no Word ou Bloco de Notas e tentar destravar. | Lista o programa que segura o arquivo; "Fechar" pede para fechar e só encerra depois de 5 s. O próprio app nunca se fecha. |
| 9.7 | Destravador: arrastar uma pasta e uma unidade. | Mostra no máximo os primeiros 4.000 arquivos; não segue junções. Um prompt dentro da pasta não aparece (limite conhecido). |
| 9.8 **[pendrive]** | Bandeja › Pendrives › Ejetar, com um arquivo aberto no pendrive e depois sem nada aberto. | Com arquivo aberto, abre o destravador na unidade; sem nada, ejeta e o Windows diz que pode remover. O disco do sistema nunca é oferecido. |

## 10. Bateria, calor e energia (Fase 11)

| # | Passo | Esperado |
|---|-------|----------|
| 10.1 | Início, em notebook: tirar e pôr o carregador. | "Agora: X% · N W saindo/entrando · dura cerca de…" muda sozinho a cada 30 s. Em desktop, anote o que o cartão mostra. |
| 10.2 | Carregador fraco (um USB-C de 15 W num notebook que pede mais). | O diagnóstico aponta carregador lento ou que não dá conta, só depois de 4 leituras seguidas. |
| 10.3 | Histórico: depois de alguns dias, "Quem mais gastou bateria" e "Saúde da bateria". | A capacidade de hoje ÷ de fábrica bate com `powercfg /batteryreport`. |
| 10.4 | Ajustes › Modo Colo e Desfazer. | O turbo some (a velocidade máxima cai) no plano ativo; Desfazer devolve o valor de antes. |
| 10.5 | Ajustes › Modo sobrevivência, com a bateria abaixo de 15% ou pelo botão. | Economizador ligado, tela principal em 60 Hz só nesta sessão, tema escuro; tudo volta ao ligar na tomada, pelo botão ou pelo Desfazer. |
| 10.6 **[admin]** | "Por que o notebook não dorme?" e "Não deixar acordar o PC…". | Lê `powercfg /requests` e a lista de dispositivos que acordam; sem administrador diz isso. O Desfazer reativa o dispositivo. |
| 10.7 | Baixar a tela para 60 Hz na bateria e suspensão seletiva USB. | A taxa volta ao ligar na tomada; a suspensão volta pelo Desfazer. |
| 10.8 | Configurações › Preto puro no tema escuro, em tela OLED. | Fundos pretos; sem Mica. Contraste do texto continua legível. |
| 10.9 | Bandeja: Plano de energia e Limpar a área de transferência; clique do meio configurável. | O plano troca em 1 clique e aparece em Ações com Desfazer. |

## 11. Videochamadas (Fase 12, leva 12.1)

| # | Passo | Esperado |
|---|-------|----------|
| 11.1 | Abrir uma chamada de vídeo (Teams, Zoom ou Meet no navegador). | O mini-widget e a bandeja mostram "Em uso agora" com o programa; some ao encerrar. |
| 11.2 | Configurações do Windows › Privacidade › Microfone: negar o acesso e abrir "Por que ninguém me ouve?". | O app diz que o microfone está bloqueado no Windows (e só para apps de área de trabalho, se for o caso). |
| 11.3 | Fone Bluetooth com microfone ligado durante uma chamada. | Aponta o perfil Hands-Free (som mono) quando é o caso. |
| 11.4 | Teste de estabilidade (cerca de 10 s) em Wi-Fi bom e em Wi-Fi ruim. | Média, jitter e perda; o veredito diz se o problema é o Wi-Fi, o provedor ou o processador. |
| 11.5 | Chamada aberta e um atualizador baixando (300 KB/s ou mais). | Um aviso por chamada; "Modo eficiência nos downloads" aplica e o Desfazer devolve. |

## 12. Rede e internet (Fase 12, leva 12.2)

| # | Passo | Esperado |
|---|-------|----------|
| 12.1 | Janela Rede e internet: "Conectado, mas não navega" com internet boa. | Todas as etapas OK. |
| 12.2 | Repetir com o cabo do roteador desligado e com um DNS inválido configurado. | Aponta a primeira etapa que falhou e oferece só a correção dela. As correções vão para Ações. |
| 12.3 | Comparar DNS (atual, Cloudflare, Google, Quad9). | Só sugere trocar quando o melhor é 20% e 10 ms mais rápido. |
| 12.4 **[admin]** | Trocar o DNS e Desfazer. | O DNS muda no adaptador (confira com `ipconfig /all`) e volta ao que era (fixo ou automático). |
| 12.5 | Wi-Fi: lista de pontos de acesso (casa com roteador de 2,4 e 5 GHz). | Aviso só quando a rede de 5 ou 6 GHz está com sinal bom. |
| 12.6 | Adaptadores de VPN desconectados (TAP, Wintun, WireGuard, OpenVPN). | Apontados com o caminho para remover no Gerenciador de Dispositivos; o app não remove sozinho. |

## 13. Wi-Fi salvo, hotspot e apresentação (Fase 12, leva 12.3)

| # | Passo | Esperado |
|---|-------|----------|
| 13.1 | Rede e internet › Redes Wi-Fi salvas. | Lista as redes com o tipo de proteção; aberta, WEP e WPA com TKIP aparecem como fracas. |
| 13.2 **[admin]** | "Mostrar QR" de uma rede com senha; ler com a câmera de um celular. | O celular oferece entrar na rede; funciona em tema claro e escuro. Sem administrador o app avisa que a senha não vem. A senha não é gravada em nenhum arquivo. |
| 13.3 | "Gerenciar redes conhecidas". | Abre as configurações do Windows (não apaga perfil pelo app). |
| 13.4 | Celular como hotspot sem marcar como "rede limitada". | Um aviso por conexão aponta o programa sem janela que mais puxa dados e ensina a marcar como limitada. |
| 13.5 | Bandeja › Modo apresentação (e o botão em Videochamadas). | Avisos do app em silêncio, ícones da área de trabalho somem, a tela não apaga e o Modo Foco liga; ao desligar ou sair do app, tudo volta. |
| 13.6 | Videochamadas › Limpar recentes…. | Esvazia Recentes, listas de atalhos, TypedPaths e RunMRU; os arquivos em si ficam. Desfazer devolve. |
| 13.7 | Verificar malware › Desvios de navegação: acrescentar ao `hosts` uma linha de teste desviando um site de banco para outro IP (e depois um proxy PAC por arquivo local). | Ambos são apontados; linhas de bloqueio de anúncios não. "Restaurar" e "Remover" guardam backup e o Desfazer devolve. |

## 14. Recuperação, malware e atualizações do Windows (Fase 8 e Prioridade 3)

| # | Passo | Esperado |
|---|-------|----------|
| 14.1 **[pendrive]** | Recuperar arquivos: apagar um arquivo conhecido do pendrive e recuperar. | Mostra prévia e chance de recuperação; o arquivo recuperado abre. Anote se o app avisa para recuperar em outra unidade. |
| 14.2 | Verificar malware: varredura rápida e de uma pasta com o arquivo de teste EICAR. | Usa o Defender; o resultado e o histórico aparecem; tudo vai para Ações. O app não tem motor próprio. |
| 14.3 | Verificar malware com o MRT. | Usa o `mrt.exe` do Windows e lê o `mrt.log`; a remoção é sempre da Microsoft. |
| 14.4 | Sistema › Drivers do fabricante, em notebook Dell, HP ou Lenovo. | O inventário, os links oficiais e as ferramentas do fabricante batem com o modelo. |
| 14.5 | Diagnóstico de atualizações do Windows. | Reinício pendente, serviço desativado, mesma atualização falhando 2 vezes e mais de 40 dias sem instalar nada são apontados quando existem. |

## 15. Instalação, atualização e privacidade (Prioridade 3 e 5)

| # | Passo | Esperado |
|---|-------|----------|
| 15.1 | Instalar o instalador da CI em uma máquina limpa e desinstalar. | Instala, abre, desinstala sem deixar o app na bandeja nem na inicialização. |
| 15.2 | Atualização do app: consulta diária de versão (se ligada). | Nenhuma outra conexão. A atualização só instala com assinatura válida; uma assinatura trocada é recusada. |
| 15.3 | Pasta de dados do app, depois de uma sessão longa. | Os arquivos de configuração, ações e histórico abrem sem erro. Matar o processo no meio de uma gravação e reabrir mantém os dados (gravação atômica). |
| 15.4 | Conferir `Textos/Privacidade.txt` (ou a tela Privacidade) contra o que o app grava e lê, na lista de arquivos da pasta de dados. | Todo arquivo e leitura novos (bateria, Wi-Fi, hosts, recentes, tamanhos de programas) estão citados. |

## 16. Arm64

| # | Passo | Esperado |
|---|-------|----------|
| 16.1 | Em um Windows Arm64 (Surface Pro X, Copilot+ PC ou máquina virtual): abrir o build Arm64. | Abre, lê os sensores que existirem e não exibe erro de arquitetura. |

## Resultados

| Data | Windows | Máquina | Itens com problema | Quem |
|------|---------|---------|--------------------|------|
|      |         |         |                    |      |
