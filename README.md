# Notebook Doctor

Aplicativo de bandeja para Windows que mostra o que deixa o notebook lento e corrige com segurança: diagnóstico em tempo real, limpeza, gerenciamento de processos e inicialização, histórico, ajustes do Windows e notificações. Interface em Material Design 3 (tema claro e escuro) com a fonte Google Sans Flex.

- .NET 8, WPF (com Windows Forms só para o ícone da bandeja)
- Suporte: https://klivfy.com/suporte

## Compilar

```powershell
dotnet build -c Release
dotnet run -c Release
```

Requer Windows 10/11 e o SDK do .NET 8. Para gerar o executável: `dotnet publish -c Release -r win-x64 --self-contained false`.

## Testes

```powershell
dotnet test tests/NotebookDoctor.Tests
```

As regras de diagnóstico, a nota de saúde, a detecção de vazamento de memória e os modelos de dados ficam em `src/NotebookDoctor.Core` (sem WPF nem chamadas ao Windows). Por isso os testes rodam em qualquer sistema. A CI (`.github/workflows/ci.yml`) roda em Linux a cada pull request: compila o app (com `EnableWindowsTargeting`) e roda os testes. O app em si só executa no Windows.

## Estrutura

- Raiz: o app WPF (janelas, bandeja, temas) e `Services/` com o que conversa com o Windows (processos, registro, powercfg, PowerShell, arquivos)
- `src/NotebookDoctor.Core`: lógica pura, testável
- `tests/NotebookDoctor.Tests`: testes xUnit do Core

## Segurança

- Nada é apagado ou encerrado sem confirmação; mudanças de sistema têm "Desfazer" na seção Ações.
- Modo simulação mostra o que cada correção faria sem alterar nada.
- O app grava apenas em `%LOCALAPPDATA%\NotebookDoctor` e, para integração com o Windows, em chaves do `HKCU`.

## Documentos

- [MELHORIAS.md](MELHORIAS.md): 150 melhorias e o status de cada uma
- [REVISAO-DESIGN.md](REVISAO-DESIGN.md): revisão de design e acessibilidade

## Licenças

A fonte Google Sans Flex segue a SIL Open Font License 1.1.

© 2026 Klivfy Tecnologia e Consultoria em TI LTDA. Todos os direitos reservados.
