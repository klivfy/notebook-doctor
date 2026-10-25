---
name: telas
description: Conferir as telas que a CI gera do Notebook Doctor (artefato "telas" e smoke.txt). Use depois de um push que mexe em XAML, estilos, textos ou tamanhos de janela, antes de dizer que uma mudança visual está pronta.
---

# Conferir as telas da CI

O job `windows` da CI abre todas as telas com `NotebookDoctor.exe --demo --smoke=pasta` nos temas claro e escuro, em português e em inglês, e publica o artefato **telas**.

1. Espere a CI do commit terminar.
2. Com o GitHub CLI: `python3 scripts/telas.py` (último run do branch atual) ou `python3 scripts/telas.py --run <id>`. As imagens ficam em `smoke-out/<commit>/`.
   Sem o `gh` (sessões na nuvem), baixe o artefato pelas ferramentas do GitHub disponíveis e leia o `smoke.txt` direto.
3. O resumo lista as linhas `aviso`, `ERRO` e `FALHAS` do `smoke.txt`: tela que não abriu, ligação quebrada, texto sem tradução e texto com cara de português em inglês. Cada uma é trabalho desta leva.
4. Abra as imagens das telas que a mudança toca e compare com `DESIGN.md`: texto cortado, botão fora da janela, contraste nos dois temas.
