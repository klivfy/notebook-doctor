# Fase 0 — Fundação e governança (transparência e cordialidade)

Este documento define a base obrigatória para as próximas fases de evolução do Notebook Doctor com foco em:

- transparência sobre decisões e efeitos das ações;
- cordialidade na linguagem com quem usa o app;
- segurança operacional (ações reversíveis e com contexto);
- medição de valor real (antes/depois).

## 1) Objetivo da fase

Criar padrões de produto e engenharia para que qualquer nova funcionalidade:

1. explique por que existe;
2. diga exatamente o que muda no computador;
3. informe risco e como desfazer;
4. comprove resultado quando aplicável.

## 2) Métricas de sucesso (baseline e acompanhamento)

As métricas abaixo são obrigatórias para as fases seguintes. Quando ainda não houver telemetria opt-in para uma métrica, o acompanhamento pode começar por amostragem em testes internos e validações de suporte.

### 2.1 Métricas de uso e confiança

- **Taxa de aceitação de recomendações**
  - Definição: `% de recomendações aplicadas / recomendações exibidas`.
  - Meta inicial: acompanhar tendência por versão (sem meta rígida na primeira entrega).

- **Taxa de desfazer por ação**
  - Definição: `% de ações revertidas / ações aplicadas` por tipo de ação.
  - Interpretação: taxa alta sugere recomendação mal calibrada, linguagem confusa ou efeito indesejado.

- **Taxa de cancelamento em confirmação**
  - Definição: `% de diálogos de confirmação cancelados`.
  - Interpretação: ajuda a detectar ações com risco percebido alto ou explicação insuficiente.

### 2.2 Métricas de resultado técnico

- **Taxa de ganho mensurável (antes/depois)**
  - Definição: `% de ações com melhora detectável no indicador esperado`.
  - Ex.: redução de RAM em segundo plano, melhora de latência de abertura, redução de temperatura média.

- **Tempo até resolução (jornadas guiadas)**
  - Definição: tempo entre iniciar jornada e concluir com status de problema resolvido ou mitigado.

### 2.3 Métricas de percepção

- **Satisfação pós-ação (microfeedback 1–5)**
  - Definição: nota simples após ação de alto impacto.
  - Uso: comparação por tipo de ação e por versão.

- **NPS/avaliação qualitativa por ciclo**
  - Definição: compilação periódica das avaliações voluntárias já existentes no app.

## 3) Definition of Done (DoD) para funcionalidades novas

Uma entrega só é considerada pronta quando cumprir todos os itens aplicáveis:

1. **Texto claro e cordial**
   - Sem jargão desnecessário.
   - Diz o que vai acontecer antes da ação.

2. **Tradução completa**
   - Todo texto visível com `Loc.T` / `Loc.F`.
   - Entradas adicionadas em `en.json` e `es.json`.

3. **Segurança operacional**
   - Ações destrutivas com confirmação (Cancelar padrão).
   - Modo simulação respeitado.
   - Registro em Ações + possibilidade de desfazer quando tecnicamente possível.

4. **Privacidade e transparência**
   - Sem coleta oculta de dados.
   - Se houver novo envio de dados, precisa de opt-in explícito e atualização dos textos de privacidade.

5. **Evidência técnica**
   - Para ação de impacto, declarar indicador esperado e como validar efeito.

6. **Qualidade de engenharia**
   - Build Release sem erro.
   - Testes relevantes passando.
   - Sem regressão nos testes arquiteturais existentes.

## 4) Priorização por importância e criticidade

Toda proposta entra em uma matriz de decisão:

- **Criticidade alta**: risco de dano, perda de dado, segurança, privacidade, confiança do usuário.
- **Importância alta**: impacto direto na resolução de lentidão, estabilidade e clareza para usuário final.

Ordem de execução padrão:

1. Transparência obrigatória da ação;
2. Segurança/reversibilidade;
3. Evidência de resultado;
4. Automação contextual;
5. Conveniências e acabamento.

## 5) Critérios de aceitação desta fase

A Fase 0 é concluída quando:

- este documento existir e estiver versionado;
- existir contrato técnico de recomendação explicável (documento dedicado);
- o plano das próximas fases referenciar explicitamente estes padrões.

## 6) Dependências para Fase 1

A Fase 1 (transparência obrigatória) deve começar com:

- contrato de recomendação explicável aprovado;
- lista inicial de ações prioritárias que receberão explicação completa;
- definição dos campos mínimos exibidos na UI (motivo, evidências, risco, reversão).
