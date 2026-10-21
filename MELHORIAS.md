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
| A22 | Verificar atualizações do Windows pendentes/travadas e reinício pendente | 🟡 reinício pendente detectado; falta checar updates travados |
| A23 | Detectar drivers com erro ou desatualizados | 🟡 lista dispositivos com driver em erro; nenhum neste PC |
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
| A39 | Manutenção agendada em horário ocioso | 🟡 limpeza semanal agora espera 10 min sem uso do computador; falta agendar outras rotinas |
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
| M03 | Overlay de desempenho para jogos | ⬜ |
| M04 | Gráficos maiores com zoom e dica ao passar o mouse | 🟡 dica com valor e horário ao passar o mouse e marcador no ponto; zoom ainda não |
| M05 | Linha do tempo por processo no histórico | ✅ (dados por programa gravados a partir desta versão) |
| M06 | Exportar histórico em CSV/JSON | ✅ (CSV no formato do Excel brasileiro) |
| M07 | Importar/exportar configurações | ⬜ |
| M08 | Alto contraste | 🟡 (cores do sistema no Alto Contraste; não testado com o modo ligado) |
| M09 | Cor de destaque do Windows (Material You) | ⬜ |
| M10 | Tamanho de fonte ajustável | 🟡 (segue o "Tamanho do texto" do Windows) |
| M11 | Suporte completo a leitores de tela (UI Automation nas abas) | ✅ (revisão de design + lote 6) |
| M12 | Navegação completa por teclado e atalhos de aba | 🟡 Ctrl+1…8 e F5 feitos |
| M13 | Multi-idioma (EN, ES) | ⬜ |
| M14 | Assistente de primeira execução | ✅ tela de boas-vindas na primeira execução com tema, iniciar com o Windows, avisos e limpeza semanal |
| M15 | Explicações "por que isso importa?" em cada alerta | ✅ (diagnóstico e Ajustes) |
| M16 | Central de ajuda embutida | 🟡 botão Suporte → klivfy.com/suporte |
| M17 | "Enviar diagnóstico ao suporte" (pacote .zip sem dados pessoais) | ✅ (fica salvo para anexar; nada é enviado sozinho) |
| M18 | Telemetria anônima opcional | ⬜ |
| M19 | Relatório de falhas opcional | ⬜ |
| M20 | Lista de exceções de alertas | ✅ |
| M21 | Lista editável de processos protegidos | ✅ |
| M22 | Agrupar processos por aplicativo | ⬜ |
| M23 | Ícones reais dos programas nas tabelas | ✅ (tabela de Processos) |
| M24 | Busca e filtro nas tabelas | ✅ busca em Processos, Inicialização e Espaço |
| M25 | Seleção múltipla e ações em lote | ⬜ |
| M26 | Menu de contexto (abrir local, propriedades, pesquisar online) | ⬜ |
| M27 | Abrir a pasta do item de inicialização | 🟡 botão "Abrir local" na aba Inicialização seleciona o arquivo no Explorador; não testado no Windows |
| M28 | Mostrar editor/assinatura do item de inicialização | 🟡 coluna Editor: quem assinou o programa ou, sem assinatura, o fabricante declarado no arquivo; não testado no Windows |
| M29 | Tarefas agendadas de terceiros, com opção de desativar | ⬜ |
| M30 | Gerenciar serviços (listar e sugerir "manual") | ⬜ |
| M31 | Listar extensões pesadas de navegador | ⬜ |
| M32 | Detectar antivírus conflitantes | ⬜ |
| M33 | Orientar OneDrive "arquivos sob demanda" | ⬜ |
| M34 | Lixeira por unidade | 🟡 uma linha de Lixeira por unidade com itens, para esvaziar só a do disco cheio; não testado no Windows |
| M35 | Limpar cache de miniaturas e ícones | 🟡 miniaturas (thumbcache) e cache de ícones na Limpeza, desmarcados; arquivos em uso são pulados; não testado no Windows |
| M36 | Limpar logs de eventos grandes (opcional) | ⬜ |
| M37 | Limpar pontos de restauração antigos (manter o último) | ⬜ |
| M38 | Caches de desenvolvimento (npm, NuGet, pip, Gradle) | ✅ npm, NuGet, Gradle, pip, Yarn e pnpm na Limpeza, desmarcados por padrão |
| M39 | Caches de apps (Spotify, Teams, Discord, Slack) | 🟡 caches de Discord, Slack, Teams (clássico e novo) e VS Code, desmarcados; Spotify fica de fora porque o cache dele guarda as músicas offline; não testado no Windows |
| M40 | Compactar disco virtual do WSL / detectar `vmmem` | ⬜ |
| M41 | Avisar RAM alta do WSL/Docker e sugerir `.wslconfig` | 🟡 alerta quando vmmem passa de 2 GB, com dica de .wslconfig e botão "Encerrar WSL"; WSL não estava rodando no teste |
| M42 | Reduzir a frequência de amostragem na bateria | ⬜ |
| M43 | Silenciar alertas em tela cheia/apresentação | 🟡 com app em tela cheia, jogo ou apresentação, o aviso vai só para o histórico (opção ligada por padrão); não testado no Windows |
| M44 | Horário silencioso | 🟡 horário silencioso configurável (aceita faixa pela meia-noite, ex.: 22:00–07:00); avisos continuam no histórico; não testado no Windows |
| M45 | Limites de alerta por métrica, com duração configurável | 🟡 minutos seguidos acima do limite configuráveis (padrão 3), valendo para CPU e agora também para RAM; não testado no Windows |
| M46 | Histórico de alertas disparados | ✅ |
| M47 | Linha do tempo de eventos (instalações, updates, picos) | ⬜ |
| M48 | Comparar hoje × semana passada | 🟡 aba Histórico compara as últimas 24 h com o mesmo horário da semana passada (CPU, RAM e nota); não testado no Windows |
| M49 | Mapa de calor: horários em que o PC fica mais lento | 🟡 aba Histórico mostra mapa de calor dia × hora (último mês) e o pior horário; não testado no Windows |
| M50 | Previsão de quando o disco enche | 🟡 aba Histórico estima quando o disco enche (tendência do último mês, ignorando o que veio antes da última limpeza); não testado no Windows |

## 🟢 Baixo impacto (50)

| # | Melhoria | Status |
|---|---|---|
| B01 | Número de CPU no ícone da bandeja | ⬜ |
| B02 | Clique simples no ícone da bandeja abre o painel | ✅ |
| B03 | Mini-métricas no menu da bandeja | ⬜ |
| B04 | Animação suave nos medidores | ⬜ |
| B05 | Efeito ripple nos botões | ⬜ |
| B06 | Transição entre abas | ⬜ |
| B07 | Lembrar posição e tamanho da janela | ⬜ |
| B08 | Lembrar a última aba aberta | ✅ |
| B09 | Opção de iniciar minimizado | ⬜ |
| B10 | Opção "fechar encerra o app" | ⬜ |
| B11 | Dicas (tooltips) nos cartões | ⬜ |
| B12 | Etapas de progresso na limpeza | ⬜ |
| B13 | Som discreto ao concluir | ⬜ |
| B14 | Mensagem de comemoração ao liberar mais de 1 GB | ⬜ |
| B15 | Modo compacto da janela | ⬜ |
| B16 | Escolha GB × GiB | ⬜ |
| B17 | Unidades °C/°F | ⬜ |
| B18 | Copiar métricas para a área de transferência | ⬜ |
| B19 | Captura de tela do painel | ⬜ |
| B20 | Exportar relatório em PDF | ⬜ |
| B21 | Atalho no Menu Iniciar pelo instalador | ⬜ |
| B22 | Botão "Verificar atualização" | ⬜ |
| B23 | Tela "Sobre" com versão, licenças e novidades | 🟡 versão, suporte e licença da fonte |
| B24 | Links de termos e privacidade | ⬜ |
| B25 | Avaliação do app dentro dele | ⬜ |
| B26 | Modo demonstração com dados fictícios | ⬜ |
| B27 | Variações de texto mais amigáveis | ⬜ |
| B28 | Ícones nas abas | ✅ (barra lateral) |
| B29 | Ordenação persistente nas tabelas | ⬜ |
| B30 | Colunas personalizáveis | ⬜ |
| B31 | Lembrar larguras de coluna | ⬜ |
| B32 | Destaque de linha por severidade | ⬜ |
| B33 | Totais no rodapé das tabelas | ⬜ |
| B34 | "Marcar/desmarcar todos" na limpeza | ⬜ |
| B35 | Prévia dos arquivos antes de apagar | ⬜ |
| B36 | "Não perguntar de novo" (só em ações não destrutivas) | ⬜ |
| B37 | Tema automático por horário | ⬜ |
| B38 | Senha para alterar configurações (modo empresa) | ⬜ |
| B39 | Documentar argumentos de linha de comando (`--tab`, `--background`) | ⬜ |
| B40 | Log de diagnóstico exportável | ⬜ |
| B41 | Abrir a pasta de dados pelo app | ✅ (botão em Sobre) |
| B42 | Redefinir configurações | ⬜ |
| B43 | Modo portátil | ⬜ |
| B44 | Compatibilidade com Windows 10 (barra de título) | ⬜ |
| B45 | Lembrar o monitor usado | ⬜ |
| B46 | Testar DPI 125/150/200% | ⬜ |
| B47 | Selo de status na barra de tarefas | ⬜ |
| B48 | Jump list (Limpeza, Processos) | ⬜ |
| B49 | Mensagem de boas-vindas na bandeja | ⬜ |
| B50 | Créditos | ⬜ |

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

M22 Agrupar processos por aplicativo · M26 Menu de contexto nas tabelas · M27 Abrir a pasta do item de inicialização · M28 Editor e assinatura do item de inicialização · M29 Tarefas agendadas de terceiros · M30 Serviços (sugerir "manual") · M31 Extensões pesadas de navegador · M32 Antivírus conflitantes · M35 Cache de miniaturas e ícones · M07 Importar/exportar configurações.
