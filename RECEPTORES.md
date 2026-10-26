# Receptores da Klivfy para o Notebook Doctor

Especificação dos dois endereços que o app já chama e que ainda precisam existir em `klivfy.com`. Enquanto não existirem, o app
trata a resposta como "não aceitou" e a pessoa não vê erro nenhum além de uma mensagem simples (avaliação) ou silêncio
(estatísticas). Os esquemas são a fonte de verdade do formato: `specs/receptores/*.schema.json`. O teste
`ReceptoresContractTests` garante que o que o app envia valida contra eles e que a lista de ferramentas é a mesma.

| | Avaliação (B25) | Estatísticas anônimas (M18) |
|---|---|---|
| Endereço | `POST https://klivfy.com/notebookdoctor/avaliacao` | `POST https://klivfy.com/notebookdoctor/estatisticas` |
| Quando o app chama | Só quando a pessoa clica em "Enviar avaliação" e confirma o texto mostrado | Só com a opção ligada (nasce desligada), 2 minutos depois de abrir o app, no máximo 1 vez por semana |
| Esquema | `specs/receptores/avaliacao.schema.json` | `specs/receptores/estatisticas.schema.json` |
| Frequência esperada | Rara (uma por pessoa que quiser) | Até 1 por computador por semana |

## 1. Contrato comum

- **Transporte:** HTTPS apenas, no host `klivfy.com` (o app recusa qualquer outro endereço e qualquer `http://`).
- **Método e corpo:** `POST`, `Content-Type: application/json; charset=utf-8`, corpo UTF-8 com no máximo **4 KB**. Sem autenticação, sem
  cookies, sem cabeçalhos próprios; o app não envia `User-Agent` personalizado. Não há `Origin` (não é navegador): **não** habilitar CORS.
- **Resposta que o app entende:** qualquer `2xx` = enviado (preferir `204 No Content`). Qualquer outro status = "não aceito". Erro de rede ou
  tempo esgotado (20 s) = "sem conexão". O app **não lê o corpo da resposta**.
- **Tentativas:** a avaliação não é reenviada sozinha (fica guardada no computador; a pessoa clica de novo). As estatísticas são
  tentadas de novo só na próxima abertura do app, e a contagem só é apagada do computador depois de um `2xx`. Por isso o receptor deve ser
  **idempotente o bastante**: um envio repetido não é um erro, só entra uma vez a mais (não há identificador para deduplicar, de propósito).
- **Códigos de erro sugeridos:** `400` corpo inválido para o esquema; `413` corpo maior que 4 KB; `415` tipo de conteúdo errado; `429`
  limite de uso; `5xx` falha do servidor. O app trata todos como "não aceito" e não insiste na hora.

## 2. Avaliação — `POST /notebookdoctor/avaliacao`

Exemplo:

```json
{
  "app": "NotebookDoctor",
  "versao": "1.1.0",
  "idioma": "pt",
  "nota": 4,
  "comentario": "Gostei, mas queria mais opções de limpeza"
}
```

| Campo | Tipo | Regra |
|---|---|---|
| `app` | texto | Sempre `"NotebookDoctor"`. |
| `versao` | texto | Versão do app, só números e pontos (`1.1.0`), até 20 caracteres. |
| `idioma` | texto | `pt`, `en` ou `es` (idioma do app, não do Windows). |
| `nota` | inteiro | De 1 a 5. |
| `comentario` | texto | **Opcional** (o campo some quando vazio). De 1 a 500 caracteres, texto puro, já sem quebras de linha e com dados pessoais trocados por marcadores (`<email>`, `<ip>`, `<mac>`, `<id>`, `<caminho>`, `<usuario>`, `<computador>`, `<pasta-pessoal>`, `<dominio>`, `<serie>`). |

Regras do receptor:

1. Validar contra o esquema; campo desconhecido, campo faltando ou valor fora da faixa = `400`. Guardar **só** os campos do esquema.
2. O comentário é **texto não confiável**: nunca interpretar como HTML nem Markdown, escapar em qualquer tela interna, tratar como possível
   spam ou ofensa. Não responder à pessoa: não há como (não existe e-mail nem identificador).
3. Guardar a data **só com o dia** (sem hora), a versão, o idioma, a nota e o comentário. Nada de IP, `User-Agent`, cookies ou cabeçalhos.

## 3. Estatísticas — `POST /notebookdoctor/estatisticas`

Exemplo:

```json
{
  "app": "NotebookDoctor",
  "versao": "1.1.0",
  "idioma": "pt",
  "windows": "11",
  "semana": "2026-W44",
  "usos": { "drivers": 1, "limpeza": 3 },
  "falhas": { "app": 2 }
}
```

| Campo | Tipo | Regra |
|---|---|---|
| `app`, `versao`, `idioma` | | Como na avaliação. |
| `windows` | texto | `"10"` ou `"11"`: só a família, sem número de compilação nem edição. |
| `semana` | texto | Semana ISO do envio, `AAAA-Www` (`2026-W44`). É a única informação de tempo; não há data nem hora. |
| `usos` | objeto | Quantas vezes cada ferramenta foi aberta desde o último envio. Chaves da lista abaixo; valores inteiros de 1 a 100000. Ferramenta sem uso não aparece. |
| `falhas` | objeto | Mesma forma. Hoje só existe a chave `app` (erros do próprio app, sem texto de erro). |

Chaves permitidas (o app só envia estas; o teste de contrato confere): `limpeza`, `limpeza-avancada`, `duplicados`, `atualizacoes`,
`drivers`, `inicializacao`, `servicos`, `tarefas`, `extensoes`, `programas`, `energia`, `backup`, `seguranca`, `recuperacao`,
`desfragmentar`, `registro`, `ferramentas-do-windows`, `apagar-de-vez`, `app`.

Regras do receptor:

1. Validar contra o esquema. Chave fora da lista = `400` (é sinal de versão adulterada ou de erro do app, e a lista fechada é uma garantia
   de privacidade: nome de programa ou de arquivo nunca entra).
2. **Agregar na hora e não guardar o envio.** Guardar só contadores somados por (`semana`, `versao`, `idioma`, `windows`, ferramenta,
   tipo = uso ou falha). Sem linha por envio, sem hora, sem IP.
3. Versões novas do app podem acrescentar ferramentas à lista; o receptor deve ser atualizado junto (`Telemetry.Tools` no código e o
   esquema mudam no mesmo commit).

## 4. Privacidade e operação (obrigatório)

Estas regras fazem parte da promessa feita à pessoa em `Textos/Privacidade.txt` e em Configurações › Privacidade:

- **Sem identificador:** nada de ID de instalação, de computador ou de pessoa, nem como cookie, nem derivado de IP.
- **IP:** pode ser lido pela infraestrutura só para o limite de uso (tabela em memória ou do provedor de borda, com expiração de até 1 hora),
  **nunca** gravado em banco, log de aplicação ou arquivo de análise. Desligar o registro de acesso do servidor para essas duas rotas ou
  mascarar o IP antes de gravar.
- **Limite de uso sugerido:** 10 requisições por hora por IP em cada rota (a resposta é `429`). O uso normal é de 1 por semana.
- **Retenção:** estatísticas agregadas podem ficar sem prazo (não identificam ninguém); avaliações com comentário, até 24 meses ou até a
  Klivfy apagar; avaliações sem comentário podem ficar agregadas.
- **Acesso:** só a equipe da Klivfy; nada é vendido nem repassado. Os dados não alimentam publicidade.
- **Política publicada:** `klivfy.com/privacidade` precisa citar as duas coletas, o que é enviado, a retenção e que a pessoa liga e
  desliga a estatística quando quiser. O texto do app (`Privacidade.txt` e traduções) já diz o que o app envia.

## 5. Como testar um receptor

Com o receptor no ar, estes comandos devem dar o resultado indicado.

```bash
# Avaliação válida: 204
curl -i -X POST https://klivfy.com/notebookdoctor/avaliacao -H 'Content-Type: application/json' \
  -d '{"app":"NotebookDoctor","versao":"1.1.0","idioma":"pt","nota":4,"comentario":"Gostei"}'

# Sem comentário: 204
curl -i -X POST https://klivfy.com/notebookdoctor/avaliacao -H 'Content-Type: application/json' \
  -d '{"app":"NotebookDoctor","versao":"1.1.0","idioma":"en","nota":5}'

# Nota fora da faixa: 400
curl -i -X POST https://klivfy.com/notebookdoctor/avaliacao -H 'Content-Type: application/json' \
  -d '{"app":"NotebookDoctor","versao":"1.1.0","idioma":"pt","nota":6}'

# Campo estranho (identificador): 400
curl -i -X POST https://klivfy.com/notebookdoctor/estatisticas -H 'Content-Type: application/json' \
  -d '{"app":"NotebookDoctor","versao":"1.1.0","idioma":"pt","windows":"11","semana":"2026-W44","usos":{},"falhas":{},"id":"x"}'

# Ferramenta fora da lista: 400
curl -i -X POST https://klivfy.com/notebookdoctor/estatisticas -H 'Content-Type: application/json' \
  -d '{"app":"NotebookDoctor","versao":"1.1.0","idioma":"pt","windows":"11","semana":"2026-W44","usos":{"chrome.exe":3},"falhas":{}}'

# Estatística válida, com contagens vazias: 204
curl -i -X POST https://klivfy.com/notebookdoctor/estatisticas -H 'Content-Type: application/json' \
  -d '{"app":"NotebookDoctor","versao":"1.1.0","idioma":"es","windows":"10","semana":"2026-W01","usos":{"limpeza":2},"falhas":{}}'

# Tipo de conteúdo errado: 415 · Corpo com mais de 4 KB: 413 · GET: 405
```

Depois de publicar: no app, Configurações › Avaliar › "Enviar avaliação" deve mostrar "Avaliação enviada. Obrigado!", e
Configurações › Privacidade › "Ver o que seria enviado" mostra o mesmo formato da seção 3.

## 6. O que ainda decide a Klivfy

1. Onde hospedar (função de borda, servidor próprio) e como mascarar o IP nas duas rotas.
2. O prazo de retenção definitivo das avaliações com comentário (a sugestão acima é 24 meses).
3. Quem lê as avaliações e como são tratadas (spam, ofensas, pedidos de apagar).
4. Publicar `klivfy.com/privacidade` com as duas coletas antes de liberar uma versão do app que as chame.
