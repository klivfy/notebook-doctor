# Notebook Doctor — 150 melhorias priorizadas

Legenda de status: ✅ feito · 🟡 parcial · ⬜ pendente.
Dentro de cada faixa, a ordem é a de urgência (valor para quem tem um notebook lento ÷ esforço/risco).

Critérios de impacto
- **Alto**: resolve ou evita lentidão, perda de dados ou falha; muda o resultado para o usuário.
- **Médio**: melhora precisão, controle, confiança ou conveniência no uso diário.
- **Baixo**: acabamento, conforto e detalhes de produto.

---

## 🔴 Alto impacto (50)

| # | Melhoria | Status |
|---|---|---|
| A01 | Botão "Otimizar agora" de um clique (limpa temporários antigos, TRIM se admin) | ✅ |
| A02 | Seletor de plano de energia (Equilibrado / Alto desempenho) + criar plano Alto desempenho | 🟡 ao ligar ou tirar da tomada, sugere o plano adequado (aviso com "Usar" ou notificação) e o diagnóstico alerta Alto desempenho na bateria; não testado no Windows |
| A03 | Limpeza de cache de navegadores (Chrome, Edge, Brave, Firefox), opt-in | ✅ |
| A04 | "Reiniciar como administrador" para limpar `C:\Windows\Temp` e rodar TRIM | ✅ |
| A05 | Exportar relatório completo em HTML (tema claro/escuro, sem scripts) | ✅ |
| A06 | Alerta de disco quase cheio (< 10% livre) | ✅ |
| A07 | Detectar vazamento de memória (processo cuja RAM só cresce ao longo do tempo) | 🟡 detecta crescimento contínuo de 300+ MB em 20+ min; não disparou no teste |
| A08 | Detectar throttling da CPU (frequência abaixo do esperado sob carga) | 🟡 compara a frequência da CPU com a máxima sob carga; não disparou no teste |
| A09 | Saúde do SSD/HDD: desgaste, erros, temperatura (SMART/StorageReliability) | 🟡 saúde do disco ok; desgaste/temperatura só em modo administrador (não testado) |
| A10 | Saúde da bateria (capacidade atual × projeto) e alerta de desgaste | ✅ 78% · 1.304 ciclos neste PC |
| A11 | Temperatura de CPU/GPU | 🟡 lê a zona térmica do Windows quando existe; este notebook não expõe sensor, então mostra nada (sensores reais exigiriam um driver de terceiros) |
| A12 | Dizer qual recurso é o gargalo agora (CPU, RAM ou disco) em linguagem simples | ✅ |
| A13 | Nota de saúde 0–100 do notebook, com evolução no histórico | ✅ nota + gráfico no Histórico |
| A14 | Botão "Corrigir" em cada recomendação do diagnóstico | 🟡 todos os achados têm botão, exceto "Disco quente" (só ventilação resolve): Processos, Gerenciador de Dispositivos, backup, relatório da bateria e reiniciar; não testado com clique |
| A15 | Modo Foco: pausar apps de segundo plano escolhidos e restaurar depois | ✅ abaixa a prioridade dos pesados em segundo plano e ativa Alto desempenho se existir; nada é fechado (não testado com clique) |
| A16 | Perfis (Trabalho, Reunião, Bateria, Desempenho) trocando plano de energia e alertas | 🟡 4 perfis (Trabalho, Reunião, Bateria, Desempenho) trocando plano, avisos e Modo Foco; não testado com clique |
| A17 | Base de conhecimento "o que é este processo / é seguro encerrar?" | ✅ ~75 programas descritos em português, coluna "Fechar?" e texto no diálogo de encerrar |
| A18 | Reduzir prioridade de apps pesados (prioridade/EcoQoS) em vez de encerrar | 🟡 botões "Baixar/Restaurar prioridade" com Desfazer na aba Ações; não testado com clique |
| A19 | Modo eficiência para apps em segundo plano | ✅ (botão em Processos; desfaz em Ações) |
| A20 | Gerenciador de inicialização com estimativa de impacto no boot | 🟡 impacto estimado (CPU acumulada e RAM do programa), não a medição do Windows |
| A21 | Medir o tempo de boot (log Diagnostics-Performance) e mostrar tendência | 🟡 pronto, mas o log do Windows não trouxe dados neste PC (mostra "–") |
| A22 | Verificar atualizações do Windows pendentes/travadas e reinício pendente | 🟡 reinício pendente; serviço Windows Update desativado; mesma atualização (KB) falhando 2+ vezes desde o último sucesso; 40+ dias sem instalar nada (menos com pausa ativa). Histórico lido pela API do Windows Update; não testado no Windows |
| A23 | Detectar drivers com erro ou desatualizados | 🟡 dispositivos com driver em erro; vídeo com o driver genérico do Windows; driver de vídeo com mais de 2 anos (adaptadores virtuais ficam de fora). Os outros drivers não são datados: data antiga em rede ou chipset não indica problema |
| A24 | Verificação de integridade (SFC/DISM) guiada, com progresso | 🟡 pronto (SFC/DISM, verificar e reparar, com log ao vivo); exige administrador e não foi executado aqui |
| A25 | Alertar processos sem assinatura digital rodando de AppData/Temp | 🟡 detecta falta de assinatura em programas de AppData/Temp/Downloads; nenhum achado neste PC |
| A26 | Verificar Defender e Firewall, com correção | 🟡 Defender (tempo real, definições), firewall e antivírus de terceiros; tudo ok neste PC, então nenhum alerta apareceu |
| A27 | Mapa de espaço em disco por pasta (descobrir o que ocupa mais) | ✅ nova aba Espaço, só leitura; testada em AppData (29,3 GB, 63 pastas) |
| A28 | Localizar arquivos grandes e duplicados, com revisão antes de apagar | 🟡 aba Espaço: maiores arquivos e duplicados (só leitura); maiores arquivos visto rodando (lento na pasta do usuário inteira); duplicados não exercitado |
| A29 | Desinstalador assistido (apps grandes ou sem uso) | 🟡 lista de aplicativos com tamanho (27 vistos) e botão que abre o desinstalador oficial; desinstalação não testada |
| A30 | Limpar cache do Windows Update e Delivery Optimization (admin) | 🟡 cache do Windows Update e Otimização de Entrega na Limpeza (desmarcados; exigem administrador) |
| A31 | Opção de desativar hibernação para liberar `hiberfil.sys` (admin) | ✅ (seção Ajustes) |
| A32 | Recomendar tamanho do arquivo de paginação | ✅ (recomenda e volta ao "gerenciado pelo Windows") |
| A33 | Efeitos visuais em "melhor desempenho" em um clique, reversível | ✅ |
| A34 | Desativar apps em segundo plano do Windows, reversível | ✅ (apps da Store) |
| A35 | Verificar o impacto da Inicialização Rápida | ✅ |
| A36 | Criar ponto de restauração antes de correções que alteram o sistema | 🟡 só com administrador e Proteção do Sistema ativa; se ela estiver desligada, avisa uma vez por sessão com botão para a configuração (não testado aqui) |
| A37 | Registro de ações com botão "Desfazer" | ✅ aba Ações; desfaz plano de energia, inicialização e plano criado |
| A38 | Modo simulação ("o que seria alterado") em toda correção | ✅ (modo simulação global + botão Simular em cada ajuste) |
| A39 | Manutenção agendada em horário ocioso | 🟡 limpeza semanal só com 10 min sem uso, fora do Modo Foco e na tomada; avisa quando a otimização semanal das unidades (TRIM) ou a manutenção automática do Windows estão desligadas, em vez de duplicar o que o Windows já faz |
| A40 | Notificações nativas do Windows (toast) com botão "Resolver" | ✅ notificações nativas do Windows com botões ("Abrir painel", "Abrir Limpeza", "Ver relatório"), sem pacotes extras; o balão da bandeja fica como reserva. Os botões só navegam no app |
| A41 | Alerta de processo novo consumindo muito por tempo prolongado, com "Encerrar" | ✅ alerta após 3 min com 25%+ da CPU, botões Encerrar e Ignorar 1 h (não disparou no teste) |
| A42 | Alerta de superaquecimento | 🟡 alerta com sensor (90 °C+ por 2 min) ou, sem sensor, quando a CPU fica abaixo de 60% da velocidade sob carga; não disparou no teste |
| A43 | Alerta de reinício recomendado (uptime + pressão de memória) | 🟡 já aparece no diagnóstico (7+ dias) |
| A44 | Identificar o app que causa picos de disco (I/O por processo) | ✅ coluna E/S KB/s em Processos; o gargalo de disco cita o app que mais movimenta dados |
| A45 | Uso de rede por aplicativo | 🟡 conexões abertas por app e tráfego total da rede no cabeçalho; banda por app exigiria administrador |
| A46 | Teste de latência/DNS/velocidade | ✅ teste de latência (roteador, 1.1.1.1, 8.8.8.8), perda e DNS em Configurações; testado: roteador 1 ms, internet 5–7 ms |
| A47 | Detalhe por processo: threads, handles, I/O, linha do tempo | 🟡 clique duplo em Processos abre PID, memória, CPU, threads, início, prioridade e caminho; não testado com clique |
| A48 | Comparativo "antes × depois" de cada otimização | 🟡 "Otimizar agora" mostra espaço livre, memória e nota antes × depois; não exercitado |
| A49 | Resumo semanal (notificação) com tendência e ações | 🟡 notificação semanal com nota, médias e programas mais pesados, e botão para o relatório; primeira em 7 dias |
| A50 | Atualização automática do app (assinada) | 🟡 (verificação diária, download com SHA-256 e assinatura Klivfy; falta publicar `versao.json` e assinar o instalador) |

## 🟠 Médio impacto (50)

| # | Melhoria | Status |
|---|---|---|
| M01 | Atalho global de teclado para abrir o painel | ✅ (Ctrl+Alt+N) |
| M02 | Mini-widget flutuante (CPU/RAM sempre visível) | ✅ |
| M03 | Overlay de desempenho para jogos | 🟡 CPU, RAM e temperatura num canto, só em tela cheia (fora do modo de apresentação) ou sempre; não recebe clique nem foco. Sem FPS; jogos em tela cheia exclusiva podem cobri-lo |
| M04 | Gráficos maiores com zoom e dica ao passar o mouse | 🟡 dica com valor e horário; nos gráficos do Histórico, roda do mouse aproxima, arrastar move, clique duplo volta, e pelo teclado +, −, setas e Home |
| M05 | Linha do tempo por processo no histórico | ✅ (dados por programa gravados a partir desta versão) |
| M06 | Exportar histórico em CSV/JSON | ✅ (CSV no formato do Excel brasileiro) |
| M07 | Importar/exportar configurações | 🟡 Configurações › Backup das preferências: exportar e importar em JSON (sem dados da máquina, como posição do widget e datas); não testado no Windows |
| M08 | Alto contraste | 🟡 (cores do sistema no Alto Contraste; não testado com o modo ligado) |
| M09 | Cor de destaque do Windows (Material You) | 🟡 opção "Usar a cor de destaque do Windows": a cor é clareada ou escurecida até ter contraste 4,5:1 com o fundo; segue a troca de cor no Windows; não testado no Windows |
| M10 | Tamanho de fonte ajustável | 🟡 (segue o "Tamanho do texto" do Windows) |
| M11 | Suporte completo a leitores de tela (UI Automation nas abas) | ✅ (revisão de design + lote 6) |
| M12 | Navegação completa por teclado e atalhos de aba | 🟡 Ctrl+1…8, F5, Ctrl+F (busca da aba), Enter (detalhe) e Delete (encerrar) em Processos, Shift+F10 abre o menu de contexto, Espaço marca a linha na Limpeza; ordem do Tab conferida contra o layout no XAML, falta testar no Windows |
| M13 | Multi-idioma (EN, ES) | 🟡 1ª leva: estrutura (`Loc` no Core, dicionários `Idiomas/en.json` e `es.json`, chave = texto em português), seletor "Idioma" em Configurações (automático pelo Windows; vale ao reabrir), números e datas no formato do idioma, e todas as telas, menus, bandeja e diálogos fixos traduzidos. Falta: mensagens montadas no código (diagnóstico, avisos, resultados), relatórios e os textos do Sobre |
| M14 | Assistente de primeira execução | ✅ tela de boas-vindas na primeira execução com tema, iniciar com o Windows, avisos e limpeza semanal |
| M15 | Explicações "por que isso importa?" em cada alerta | ✅ (diagnóstico e Ajustes) |
| M16 | Central de ajuda embutida | 🟡 botão Suporte → klivfy.com/suporte |
| M17 | "Enviar diagnóstico ao suporte" (pacote .zip sem dados pessoais) | ✅ (fica salvo para anexar; nada é enviado sozinho) |
| M18 | Telemetria anônima opcional | ⬜ depende de um servidor da Klivfy para receber e da política de privacidade publicada |
| M19 | Relatório de falhas opcional | 🟡 opt-in em Configurações; fica no PC sem dados pessoais, vai no pacote de suporte e o app avisa na abertura seguinte. Sem envio automático |
| M20 | Lista de exceções de alertas | ✅ |
| M21 | Lista editável de processos protegidos | ✅ |
| M22 | Agrupar processos por aplicativo | 🟡 opção na aba Processos (ligada por padrão): lista fechada de famílias (Steam, Acrobat, Teams, Zoom, Docker, NVIDIA…); processos compartilhados (WebView2, node) ficam separados; encerrar, prioridade, eficiência e exceções valem para todos os processos da linha |
| M23 | Ícones reais dos programas nas tabelas | ✅ (tabela de Processos) |
| M24 | Busca e filtro nas tabelas | ✅ busca em Processos, Inicialização e Espaço |
| M25 | Seleção múltipla e ações em lote | 🟡 seleção múltipla (Ctrl/Shift) em Processos e "Encerrar" com uma confirmação para todos; protegidos ficam de fora; não testado no Windows |
| M26 | Menu de contexto (abrir local, propriedades, pesquisar online) | 🟡 clique direito em Processos: detalhes, abrir local, pesquisar na internet, prioridade, modo eficiência, exceção de aviso e encerrar; não testado no Windows |
| M27 | Abrir a pasta do item de inicialização | 🟡 botão "Abrir local" na aba Inicialização seleciona o arquivo no Explorador; não testado no Windows |
| M28 | Mostrar editor/assinatura do item de inicialização | 🟡 coluna Editor: quem assinou o programa ou, sem assinatura, o fabricante declarado no arquivo; não testado no Windows |
| M29 | Tarefas agendadas de terceiros, com opção de desativar | 🟡 aba Ajustes lista as tarefas ativas fora de \Microsoft\ (COM do Agendador), com quando rodam e recomendação; proteção, hardware e atualizadores ficam; "Desativar" com confirmação e desfazer em Ações |
| M30 | Gerenciar serviços (listar e sugerir "manual") | 🟡 aba Ajustes lista serviços de terceiros que iniciam sozinhos, com recomendação; "Mudar para manual" exige administrador e tem Desfazer; serviços do Windows, segurança, drivers e rede nunca são alterados; não testado no Windows |
| M31 | Listar extensões pesadas de navegador | 🟡 Chrome, Edge, Brave, Vivaldi e Firefox, lidos dos arquivos do perfil; peso estimado (sempre carregada, todos os sites) porque o navegador não informa memória por extensão; botão abre a página de extensões |
| M32 | Detectar antivírus conflitantes | 🟡 diagnóstico avisa quando há dois antivírus com proteção em tempo real ligada (inclui o Defender), pelo productState da Central de Segurança; não testado no Windows |
| M33 | Orientar OneDrive "arquivos sob demanda" | 🟡 achado quando o OneDrive tem 20 GB+ baixados, ou 3 GB+ com menos de 15% de disco livre; conta pelos atributos dos arquivos (nada é baixado), a cada 6 h, até 20 s |
| M34 | Lixeira por unidade | 🟡 uma linha de Lixeira por unidade com itens, para esvaziar só a do disco cheio; não testado no Windows |
| M35 | Limpar cache de miniaturas e ícones | 🟡 miniaturas (thumbcache) e cache de ícones na Limpeza, desmarcados; arquivos em uso são pulados; não testado no Windows |
| M36 | Limpar logs de eventos grandes (opcional) | 🟡 aba Ajustes, como administrador: logs acima de 32 MB, com confirmação e simulação; o de Segurança nunca entra; botão para o Visualizador de Eventos (salvar cópia antes) |
| M37 | Limpar pontos de restauração antigos (manter o último) | 🟡 aba Ajustes, como administrador: mostra quantos pontos e o espaço das cópias de sombra; apaga todos menos o mais recente (SRRemoveRestorePoint), com confirmação e simulação |
| M38 | Caches de desenvolvimento (npm, NuGet, pip, Gradle) | ✅ npm, NuGet, Gradle, pip, Yarn e pnpm na Limpeza, desmarcados por padrão |
| M39 | Caches de apps (Spotify, Teams, Discord, Slack) | 🟡 caches de Discord, Slack, Teams (clássico e novo) e VS Code, desmarcados; Spotify fica de fora porque o cache dele guarda as músicas offline; não testado no Windows |
| M40 | Compactar disco virtual do WSL / detectar `vmmem` | 🟡 diagnóstico avisa disco virtual do WSL/Docker com 20 GB+ e explica como compactar (não compacta sozinho); não testado no Windows |
| M41 | Avisar RAM alta do WSL/Docker e sugerir `.wslconfig` | 🟡 alerta quando vmmem passa de 2 GB, com dica de .wslconfig e botão "Encerrar WSL"; WSL não estava rodando no teste |
| M42 | Reduzir a frequência de amostragem na bateria | 🟡 em segundo plano: 15 s na tomada, 30 s na bateria, 60 s com economia de bateria ou carga ≤ 20%; à vista continua 3 s. A nota usa no máximo os últimos 15 min |
| M43 | Silenciar alertas em tela cheia/apresentação | 🟡 com app em tela cheia, jogo ou apresentação, o aviso vai só para o histórico (opção ligada por padrão); não testado no Windows |
| M44 | Horário silencioso | 🟡 horário silencioso configurável (aceita faixa pela meia-noite, ex.: 22:00–07:00); avisos continuam no histórico; não testado no Windows |
| M45 | Limites de alerta por métrica, com duração configurável | 🟡 minutos seguidos acima do limite configuráveis (padrão 3), valendo para CPU e agora também para RAM; não testado no Windows |
| M46 | Histórico de alertas disparados | ✅ |
| M47 | Linha do tempo de eventos (instalações, updates, picos) | 🟡 aba Histórico: linha do tempo com avisos (inclusive silenciados), ações do app, instalações e picos de CPU/RAM de 5+ min; não testado no Windows |
| M48 | Comparar hoje × semana passada | 🟡 aba Histórico compara as últimas 24 h com o mesmo horário da semana passada (CPU, RAM e nota); não testado no Windows |
| M49 | Mapa de calor: horários em que o PC fica mais lento | 🟡 aba Histórico mostra mapa de calor dia × hora (último mês) e o pior horário; não testado no Windows |
| M50 | Previsão de quando o disco enche | 🟡 aba Histórico estima quando o disco enche (tendência do último mês, ignorando o que veio antes da última limpeza); não testado no Windows |

## 🟢 Baixo impacto (50)

| # | Melhoria | Status |
|---|---|---|
| B01 | Número de CPU no ícone da bandeja | 🟡 opcional; quadrado na cor do estado com o número em contraste ≥ 4,5:1 |
| B02 | Clique simples no ícone da bandeja abre o painel | ✅ |
| B03 | Mini-métricas no menu da bandeja | 🟡 CPU, RAM, temperatura, disco livre e nota no topo do menu; clicar abre o painel |
| B04 | Animação suave nos medidores | 🟡 barras do Painel deslizam até o valor novo; desligado com "Efeitos de animação" do Windows ou Alto Contraste |
| B05 | Efeito ripple nos botões | ⬜ |
| B06 | Transição entre abas | 🟡 fade com leve subida (180–220 ms); mesma regra de acessibilidade |
| B07 | Lembrar posição e tamanho da janela | 🟡 posição, tamanho e maximizada; reabre centralizada se a barra de título ficaria fora da tela |
| B08 | Lembrar a última aba aberta | ✅ |
| B09 | Opção de iniciar minimizado | 🟡 aberto pelo atalho, começa minimizado na barra de tarefas (ao iniciar com o Windows já fica só na bandeja) |
| B10 | Opção "fechar encerra o app" | 🟡 em Configurações; desligado, o X continua levando para a bandeja |
| B11 | Dicas (tooltips) nos cartões | 🟡 nota, gargalo, processador, memória, disco e bateria; o Narrador lê a mesma explicação |
| B12 | Etapas de progresso na limpeza | 🟡 "Limpando 2 de 5: Cache do Chrome…" |
| B13 | Som discreto ao concluir | 🟡 opcional (desligado por padrão); som de aviso suave do Windows ao terminar limpeza ou otimização |
| B14 | Mensagem de comemoração ao liberar mais de 1 GB | 🟡 com a conta em fotos de celular |
| B15 | Modo compacto da janela | 🟡 opção em Configurações: menu só com ícones, linhas de tabela de 32 px e cartões com menos respiro; troca na hora |
| B16 | Escolha GB × GiB | 🟡 GB como o Windows (1024), GB como a caixa do disco (1000) ou GiB; vale para tamanhos em GB e para Pt.Bytes. Colunas "(MB)" das tabelas continuam com esse título |
| B17 | Unidades °C/°F | 🟡 em todo texto (painel, diagnóstico, avisos, overlay, relatórios); as regras continuam em °C |
| B18 | Copiar métricas para a área de transferência | 🟡 botão "Copiar resumo" no Painel: nota, uso atual, gargalo, problemas e programas mais pesados; não testado no Windows |
| B19 | Captura de tela do painel | 🟡 "Salvar imagem do painel": PNG do painel inteiro (inclusive fora da tela), também copiado |
| B20 | Exportar relatório em PDF | 🟡 botão "Exportar em PDF" no Painel, gerado pelo próprio app (sem biblioteca externa, texto em Helvetica); não testado no Windows |
| B21 | Atalho no Menu Iniciar pelo instalador | 🟡 ainda sem instalador: botão em Configurações cria, atualiza (app mudou de pasta) ou remove o atalho na pasta Programas do usuário, sem administrador |
| B22 | Botão "Verificar atualização" | 🟡 "Procurar atualização" no Sobre; falta publicar o versao.json em klivfy.com |
| B23 | Tela "Sobre" com versão, licenças e novidades | 🟡 versão, suporte, Novidades e Licenças (textos embutidos em Textos/) |
| B24 | Links de termos e privacidade | 🟡 "Seus dados e privacidade" embutido no app; faltam os termos de uso e o endereço da política publicada |
| B25 | Avaliação do app dentro dele | ⬜ |
| B26 | Modo demonstração com dados fictícios | 🟡 `--demo`: Painel, Processos e Histórico (7 dias) fictícios, simulação sempre ligada, pasta temporária própria. Limpeza, Espaço, Inicialização e Ajustes ainda leem o PC real (só leitura) |
| B27 | Variações de texto mais amigáveis | 🟡 "Tudo certo" no diagnóstico e o resultado da limpeza variam a frase de apoio por dia; títulos fixos (relatórios e Narrador) |
| B28 | Ícones nas abas | ✅ (barra lateral) |
| B29 | Ordenação persistente nas tabelas | 🟡 todas as tabelas; volta ao abrir e a cada atualização de Processos |
| B30 | Colunas personalizáveis | 🟡 clique direito no título de qualquer tabela mostra ou esconde colunas; a que identifica a linha é fixa; "Mostrar todas as colunas" em Configurações. Fica fora da exportação de preferências, como as larguras |
| B31 | Lembrar larguras de coluna | 🟡 só as colunas que a pessoa ajustou, salvas ao fechar a janela |
| B32 | Destaque de linha por severidade | 🟡 barra vermelha/laranja em Processos; impacto alto em semi-negrito na Inicialização; o Narrador lê "uso alto" |
| B33 | Totais no rodapé das tabelas | 🟡 Processos, Espaço e Inicialização (respeitam a busca); a Limpeza já mostra os totais no título |
| B34 | "Marcar/desmarcar todos" na limpeza | 🟡 "Marcar todos" (exceto a Lixeira) e "Desmarcar todos" na Limpeza; não testado no Windows |
| B35 | Prévia dos arquivos antes de apagar | 🟡 "Ver arquivos" ou clique duplo: até 200 maiores, com tamanho e data; a Lixeira abre na tela do Windows |
| B36 | "Não perguntar de novo" (só em ações não destrutivas) | 🟡 reabrir como administrador e criar plano de energia; "Voltar a pedir todas as confirmações" em Configurações |
| B37 | Tema automático por horário | 🟡 "Claro de dia, escuro à noite" com horários configuráveis; troca sozinho com o app aberto |
| B38 | Senha para alterar configurações (modo empresa) | 🟡 salvar/importar/redefinir preferências, trocar de perfil, desligar a simulação e "Iniciar com o Windows" pedem a senha (vale 10 min; 5 erros = 30 s de espera). Só o hash PBKDF2-SHA256 fica guardado, fora da exportação. A TI pode fixar a senha pela política em HKLM (só administrador remove) |
| B39 | Documentar argumentos de linha de comando (`--tab`, `--background`) | ✅ README, seção Linha de comando |
| B40 | Log de diagnóstico exportável | 🟡 .txt sem dados pessoais com diagnóstico, erros, falhas, ações e avisos |
| B41 | Abrir a pasta de dados pelo app | ✅ (botão em Sobre) |
| B42 | Redefinir configurações | 🟡 "Redefinir para o padrão" com confirmação; mantém datas e posição do widget; não testado no Windows |
| B43 | Modo portátil | 🟡 com "portatil.txt" ao lado do executável, os dados ficam na pasta "dados" do app (o registro ainda é usado para iniciar com o Windows e para as notificações); não testado no Windows |
| B44 | Compatibilidade com Windows 10 (barra de título) | 🟡 barra de título escura também no Windows 10 1809–1909 (atributo antigo do DWM); a cor da barra continua só no Windows 11; não testado no Windows 10 |
| B45 | Lembrar o monitor usado | 🟡 junto com B07; se o monitor não estiver conectado, abre no principal |
| B46 | Testar DPI 125/150/200% | ⬜ |
| B47 | Selo de status na barra de tarefas | 🟡 "!" vermelho com problema, laranja com ponto de atenção, sem selo com tudo certo; dica com nota e contagem |
| B48 | Jump list (Limpeza, Processos) | 🟡 Limpeza, Processos e Histórico; com o app aberto, troca a aba da janela que já está aberta |
| B49 | Mensagem de boas-vindas na bandeja | 🟡 na primeira vez que a janela vai para a bandeja |
| B50 | Créditos | 🟡 "Licenças e créditos" no Sobre |

---

## Verificação do lote 3

- **Visto na tela:** Painel com botão Modo Foco, aba Processos com "O que é" e "Fechar?", aba Inicialização com impacto estimado, aba Espaço com análise real do AppData.
- **Não exercitado:** clique no Modo Foco, SFC/DISM (exigem administrador), detecção de vazamento, throttling e programas sem assinatura (nenhum caso real ocorreu durante o teste).
- **Mudança de abas:** agora são 8 (Painel, Processos, Limpeza, Espaço, Inicialização, Histórico, Ações, Configurações); atalhos Ctrl+1…8.

## Verificação do lote 4

- **Visto na tela:** caches de dev e do Windows Update na Limpeza, coluna E/S em Processos, aba Espaço com aplicativos instalados e com a busca de maiores arquivos rodando.
- **Não exercitado:** perfis, prioridade, desinstalar, duplicados, antes × depois (precisam de clique), alertas de segurança (nada errado neste PC) e temperatura (sem sensor).
- **Regressão encontrada e corrigida:** com os caches de dev, a análise da Limpeza passou a levar mais de 20 s. Agora cada local aparece assim que é medido, e a nota de saúde e o "Otimizar agora" medem só os temporários seguros.
- **Números em pt-BR:** a janela usa `Language="pt-BR"` (vírgula decimal, ponto de milhar).

## Verificação do lote 5

- **Visto funcionando:** tela de boas-vindas; link `notebookdoctor:tab/1` com o app aberto (a 2ª instância repassou ao app aberto, que trocou de aba, e saiu); notificação nativa entregue na central do Windows; colunas Conexões e E/S; tráfego de rede no cabeçalho; busca; teste de conexão.
- **Não exercitado:** clique duplo em Processos, dicas dos gráficos (precisam do mouse), resumo semanal (só daqui a 7 dias), superaquecimento e WSL (sem caso real).
- **Registro do Windows:** na abertura, o app cria em HKCU o ID de notificação `Klivfy.NotebookDoctor` e o link `notebookdoctor:` (apontando para o executável atual).

## Verificação do lote 6

- **Visto funcionando:** ícones dos programas e a descrição sob o nome em Processos; mini-widget com nota, barras e o programa mais pesado; Configurações com mini-widget, atualização e Exceções; linha do tempo por programa (chrome em 1º, com média, pico e resumo para o Narrador); avisos disparados no Histórico; pacote para o suporte com 8 arquivos e sem nome de usuário, nome do computador ou pasta pessoal (conferido abrindo o .zip); "Procurar atualização" respondendo "Nenhuma atualização publicada" (o endereço `klivfy.com/notebookdoctor/versao.json` ainda não existe).
- **Não exercitado:** baixar e instalar uma atualização (não há versão publicada nem instalador assinado), "Não avisar sobre este" e Exportar CSV (precisam de clique; o mesmo código do pacote foi testado), arrastar o mini-widget.
- **Bugs achados e corrigidos no teste:**
  - a pasta Documentos recusou a gravação (Acesso controlado a pastas do Defender). Relatórios, CSV e pacotes agora caem em `%LOCALAPPDATA%\NotebookDoctor\exportacoes` quando isso acontece;
  - um valor `NaN` na posição do mini-widget fazia o `settings.json` deixar de ser salvo, em silêncio. Agora a posição é opcional e falhas de gravação vão para `erros.log`;
  - um erro ao aplicar o tema com o app encerrando (tela de boas-vindas + Sair), registrado em `erros.log` às 01:20;
  - colunas espremidas em Processos: a descrição virou a 2ª linha do nome e os cabeçalhos quebram entre palavras.
- **Atualização (A50), o que falta do lado da Klivfy:** publicar `https://klivfy.com/notebookdoctor/versao.json` com `{"version":"1.2.0","url":"https://klivfy.com/…/setup.exe","sha256":"…","notes":"…"}` e assinar o instalador com um certificado de assinatura de código em nome da Klivfy. Sem isso, o app nunca instala nada.
- **Atenção no teste:** o PC registrou 92–97 °C de forma contínua durante a sessão (três avisos de superaquecimento).

## Verificação do lote 7

- **Nova seção Ajustes** (Ctrl+8), entre Ações e Configurações; Configurações passou a ser Ctrl+9. Cada ajuste mostra o estado atual, "por que importa", um selo (Recomendado/Opcional/Nada a fazer), o aviso de administrador e os botões de aplicar e Simular. Ajustes de sistema criam ponto de restauração (com administrador) e todos têm Desfazer em Ações.
- **Lido neste PC:** hibernação ligada com `hiberfil.sys` de 6,3 GB; Inicialização Rápida ligada; paginação gerenciada pelo Windows (1,8 GB); 1 de 9 efeitos visuais ligado (transparência); apps da Store em segundo plano permitidos.
- **Visto funcionando:** "Simular" mostra o que seria alterado; com o modo simulação ligado, "Usar melhor desempenho" só mostrou a simulação (as sombras continuaram iguais, conferido pela API do Windows) e o chip "Simulação ligada" apareceu no cabeçalho; Ctrl+Alt+N esconde e mostra a janela (outro programa não conseguiu registrar a mesma combinação); "Por que isso importa?" abre e continua aberto entre as atualizações, inclusive acionado pelo leitor de tela.
- **Não exercitado (mudariam o seu Windows de verdade):** aplicar os ajustes, o modo eficiência num programa real e o Alto Contraste ligado.
- **Bug achado no teste:** a explicação fechava sozinha a cada 3 s e não respondia ao leitor de tela. Agora fica aberta e usa Checked/Unchecked.
- **Atenção neste PC:** o diagnóstico mostra a proteção em tempo real do Windows desativada, sem outro antivírus, e a CPU em 92–97 °C.

## Próximo lote sugerido

M13 Multi-idioma, 2ª e 3ª levas (mensagens do código, diagnóstico, relatórios e textos do Sobre). Dependem da Klivfy: ciclo de release (versao.json e assinatura), B24 termos e política publicada, M18 telemetria, B25 avaliação do app. B05 Efeito ripple só se combinar com o visual do Windows 11.
