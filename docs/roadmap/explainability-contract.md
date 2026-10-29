# Contrato técnico — Recomendação explicável

Este contrato define o formato mínimo de uma recomendação no Notebook Doctor para garantir transparência, cordialidade e segurança.

## 1) Escopo

Aplica-se a qualquer recomendação exibida para a pessoa usuária que:

- sugira mudança de configuração;
- execute ação de manutenção;
- altere comportamento do sistema;
- peça decisão entre alternativas com impacto.

## 2) Campos obrigatórios da recomendação

Toda recomendação deve expor os campos abaixo (na UI e/ou em detalhes da ação):

1. **Título curto**
   - Frase simples, orientada a resultado.

2. **Motivo (por que isso foi sugerido)**
   - Explicação em linguagem não técnica.

3. **Evidências observadas**
   - Lista objetiva de sinais detectados pelo app.
   - Ex.: uso alto de RAM em segundo plano, erro recorrente, serviço desnecessário em auto-início.

4. **Mudança proposta (o que vai mudar)**
   - Lista explícita de alterações.
   - Sem texto genérico como “otimizar sistema”.

5. **Risco/impacto esperado**
   - Classificação: Baixo, Médio ou Alto.
   - Texto curto do possível efeito colateral.

6. **Reversão (como desfazer)**
   - Se houver desfazer automático, indicar onde.
   - Se não houver, descrever limite com honestidade.

7. **Confiança da recomendação**
   - Classificação: Alta, Média ou Baixa.
   - Baseada em qualidade/consistência dos sinais.

8. **Fonte de execução**
   - Indicar se a ação é somente sugestão, simulação ou execução real.

## 3) Regras de comportamento

1. **Sem ação opaca**
   - Nenhuma ação relevante deve ocorrer sem a pessoa saber o que muda.

2. **Sem promessa absoluta**
   - Evitar termos como “garantido”, “100%”, “corrige tudo”.
   - Quando houver incerteza, declarar.

3. **Modo simulação primeiro em cenários sensíveis**
   - Para ações de risco médio/alto, oferecer simulação quando aplicável.

4. **Confirmação obrigatória em ação destrutiva**
   - Diálogo com cancelar como padrão.

5. **Registro em histórico de ações**
   - Toda execução deve ser auditável, com contexto suficiente.

## 4) Mapeamento mínimo para implementação

### 4.1 Contrato de dados (referência)

Campos mínimos recomendados para um modelo de recomendação (nomes exemplificativos):

- `Id`
- `Title`
- `Reason`
- `Evidence[]`
- `ProposedChanges[]`
- `RiskLevel`
- `ConfidenceLevel`
- `UndoInfo`
- `ExecutionMode` (Suggest/Simulate/Apply)

### 4.2 Contrato de UI

A tela deve mostrar, no mínimo:

- título,
- motivo,
- mudança proposta,
- risco,
- botão de ação,
- acesso rápido para “como desfazer”.

Detalhes avançados (evidências completas) podem ficar em área expansível.

## 5) Critérios de aceite por recomendação

Uma recomendação só pode ser considerada pronta quando:

- tiver todos os campos obrigatórios preenchidos;
- estiver com textos traduzidos (`Loc.T` / `Loc.F`, `en.json`, `es.json`);
- estiver coberta por teste de regra (quando houver lógica de decisão);
- não violar as regras de privacidade e segurança do projeto.

## 6) Não objetivos

Este contrato não define:

- layout visual final de cada card;
- algoritmo exato de score de confiança;
- telemetria de produto.

Esses pontos são tratados nas fases de implementação específicas.
