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

## Linha de comando

O app tem uma instância só: abrir de novo com argumentos repassa o pedido para a que já está aberta.

| Argumento | O que faz |
|---|---|
| `--tab=nome` | Abre na aba: `painel`, `processos`, `limpeza`, `espaco`, `programas`, `inicializacao`, `historico`, `acoes`, `ajustes` ou `configuracoes`. O número da aba também funciona (0 = Painel, 9 = Configurações), mas muda quando entra uma aba nova; a jump list usa o nome. |
| `--background` | Inicia só na bandeja, sem abrir a janela (usado ao iniciar com o Windows). |
| `--demo` | Modo demonstração: Painel, Processos e Histórico com dados fictícios, simulação sempre ligada e dados numa pasta temporária. Abre mesmo com o app de verdade rodando. Para apresentações e capturas de tela. |
| `notebookdoctor:open` | Abre o painel. Também aceita `tab/N`, `heavy`, `report` (exporta o relatório) e `update`. São os links das notificações; funcionam com `start notebookdoctor:tab/2`. |

`--wait-parent=PID` é interno: o app usa ao reabrir como administrador.

## Testes

```powershell
dotnet test tests/NotebookDoctor.Tests
```

As regras de diagnóstico, a nota de saúde, a detecção de vazamento de memória e os modelos de dados ficam em `src/NotebookDoctor.Core` (sem WPF nem chamadas ao Windows). Por isso os testes rodam em qualquer sistema. A CI (`.github/workflows/ci.yml`) roda em Linux a cada pull request: compila o app (com `EnableWindowsTargeting`) e roda os testes. O app em si só executa no Windows.

## Estrutura

- Raiz: o app WPF (janelas, bandeja, temas) e `Services/` com o que conversa com o Windows (processos, registro, powercfg, PowerShell, arquivos)
- `MainWindow.xaml.cs`: o que é comum às abas (navegação, avisos, modo simulação); cada aba tem o seu `MainWindow.<Aba>.cs`
- `src/NotebookDoctor.Core`: lógica pura, testável
- `Textos/`: novidades, licenças e "Seus dados e privacidade", embutidos no executável e mostrados no Sobre
- `tests/NotebookDoctor.Tests`: testes xUnit do Core

## Segurança

- Nada é apagado ou encerrado sem confirmação; mudanças de sistema têm "Desfazer" na seção Ações.
- Modo simulação mostra o que cada correção faria sem alterar nada.
- O app grava apenas em `%LOCALAPPDATA%\NotebookDoctor` e, para integração com o Windows, em chaves do `HKCU`.
- Modo portátil: com um arquivo `portatil.txt` ao lado do executável, os dados ficam na pasta `dados` junto do app (o `HKCU` ainda é usado para iniciar com o Windows e para as notificações).
- Sem telemetria. Com a opção ligada, relatórios de falha ficam na pasta `falhas`, sem nome de usuário, nome do computador nem pasta pessoal, e só saem no pacote de suporte que a pessoa anexar.

## Modo empresa

Em **Configurações › Senha das configurações** dá para exigir uma senha para salvar, importar ou redefinir as preferências, trocar de perfil, desligar o modo simulação e mudar "Iniciar com o Windows". Ver, analisar e limpar continuam livres. Depois de digitada, a senha vale por 10 minutos; cinco erros seguidos fazem o app esperar 30 segundos.

O app guarda só um hash PBKDF2-SHA256 (600 mil iterações, sal aleatório) nas preferências do usuário, fora da exportação. Quem tem acesso à pasta de dados consegue apagar essa senha. Para valer de verdade, a TI grava o código no registro, onde só administradores mexem:

```
HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Klivfy\NotebookDoctor
  SettingsLock (REG_SZ) = pbkdf2-sha256$600000$...$...
```

O código vem do botão **Copiar para a política do Windows**, depois de definir a senha no app. Com a política, a senha vale para todos os usuários do computador e o app não deixa trocá-la nem removê-la.

Limite: a senha protege as telas do app. Quem editar o arquivo de preferências à mão (`settings.json`, na pasta de dados do usuário) ainda muda as opções. Ela evita mudanças casuais e por engano; não substitui as permissões do Windows.

## Documentos

- [MELHORIAS.md](MELHORIAS.md): 150 melhorias e o status de cada uma
- [REVISAO-DESIGN.md](REVISAO-DESIGN.md): revisão de design e acessibilidade

## Licenças

A fonte Google Sans Flex segue a SIL Open Font License 1.1.

© 2026 Klivfy Tecnologia e Consultoria em TI LTDA. Todos os direitos reservados.
