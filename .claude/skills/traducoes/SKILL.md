---
name: traducoes
description: Traduzir para inglês e espanhol os textos novos do Notebook Doctor. Use depois de criar ou mudar qualquer texto visível (Loc.T, Loc.F, Pt.N, ShowSnack, Simulate, XAML) ou quando o LocTests acusar "Sem tradução".
---

# Traduções do Notebook Doctor

A chave é o texto em português; `src/NotebookDoctor.Core/Idiomas/en.json` e `es.json` guardam a tradução.

1. Liste o que falta: `python3 scripts/traducoes.py faltando`. Textos do XAML aparecem no `LocTests` (`dotnet test tests/NotebookDoctor.Tests -c Release -p:EnableWindowsTargeting=true --filter LocTests`).
2. Escreva um JSON na pasta de rascunho: `{"texto em português": ["inglês", "espanhol"]}`.
   - Mesmos `{0}`, `{1:N0}` da chave.
   - Tecla de acesso (`_`) nas duas traduções, sem repetir a letra de outro item do mesmo menu.
   - Termos do Windows como o Windows chama em cada idioma (Lixeira = Recycle Bin / Papelera de reciclaje).
3. Junte: `python3 scripts/traducoes.py juntar caminho/do/arquivo.json`. Chave que já existe não é trocada.
4. Rode o `LocTests` de novo até passar.
