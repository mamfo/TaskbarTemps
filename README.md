# TaskbarTemps

Widget nativo para Windows 11 que mostra, junto ao relógio da barra de tarefas:

- temperatura e **utilização do CPU**;
- **utilização da RAM** (e temperatura, quando o módulo tem sensor);
- temperatura e **utilização da GPU**;
- **velocidade do disco**: leitura e escrita;
- **tráfego de rede**: velocidade de receção e de envio.

```text
CPU 49° 12%  ·  RAM 51%  ·  GPU 39° 8%  ·  DISCO L1,2M E300K  ·  REDE ▼1,2M ▲40K
```

- Sem instalador externo, sem .NET SDK e sem dependências para além das DLLs incluídas.
- Interface em português de Portugal.
- Tema claro/escuro automático, seguindo a definição do Windows.
- Fundo transparente: o widget assume sempre a cor da barra de tarefas.
- **Atualizações pelo GitHub**: verificação automática e instalação a partir do menu.

---

## Requisitos

| Requisito | Detalhe |
|---|---|
| Sistema | Windows 11 (testado na versão 26H2, build 26300) |
| Runtime | .NET Framework 4.8 (incluído no Windows 11) |
| Permissões | Administrador — necessário para ler a temperatura do CPU |
| PawnIO | Driver assinado necessário para os sensores do CPU |

---

## Instalação

### Com o instalador (recomendado)

Execute **`TaskbarTempsSetup.exe`** e siga o assistente. O instalador:

1. copia a aplicação para `C:\Program Files\TaskbarTemps`;
2. instala o **PawnIO 2.2.0** se ainda não estiver presente (silenciosamente);
3. cria o atalho **TaskbarTemps** no Menu Iniciar;
4. cria a tarefa agendada de arranque automático (opcional);
5. registra a aplicação em **Aplicações instaladas**;
6. inicia o widget.

Opções de linha de comandos:

```powershell
TaskbarTempsSetup.exe /S                     # instalação silenciosa com opções predefinidas
TaskbarTempsSetup.exe /S /startup:0          # sem arranque automático
TaskbarTempsSetup.exe /S /shortcut:0         # sem atalho no Menu Iniciar
TaskbarTempsSetup.exe /dir:D:\Apps\TaskbarTemps
```

Para atualizar, execute novamente o instalador. As definições em
`%APPDATA%\TaskbarTemps` são preservadas.

### Manualmente (pasta `dist`)

1. Copie a pasta `dist` para o local definitivo.
2. Instale o PawnIO (uma única vez, requer administrador):

   ```powershell
   .\PawnIO_setup.exe -install -silent
   ```

   Sem o PawnIO, a temperatura do CPU aparece como `--`, mesmo com a aplicação
   elevada, porque o acesso ao sensor depende do driver.
3. Execute `TaskbarTemps.exe` e aceite o pedido de elevação (UAC).

> O instalador do PawnIO vem de <https://github.com/namazso/PawnIO.Setup/releases>
> (versão 2.2.0) e está assinado digitalmente.

---

## Atualizações

As versões são publicadas em **<https://github.com/mamfo/TaskbarTemps/releases>**.
O instalador `TaskbarTempsSetup.exe` é autónomo: inclui a aplicação, as DLLs e o
driver PawnIO, e preserva as definições em `%APPDATA%\TaskbarTemps`.

- **Verificação automática** — 20 segundos depois de arrancar e, depois, a cada
  6 horas. Se existir uma versão nova, aparece um aviso no ícone da bandeja.
- **Procurar atualizações** (menu) — consulta a última release; se houver uma
  versão mais recente, descarrega o instalador, pede confirmação e reinicia o
  widget já atualizado.
- **Procurar atualizações automaticamente** (menu) — liga/desliga a verificação
  periódica. A preferência fica em `settings.ini`.

A verificação consulta a API pública do GitHub e não envia dados do utilizador.

### Publicar uma nova versão

1. Atualize a versão em `src/AssemblyInfo.cs`, `setup/SetupInfo.cs` e
   `setup/Setup.cs` (`AppVersion`).
2. Crie e envie a etiqueta:

   ```powershell
   git tag v1.4.1
   git push origin v1.4.1
   ```

O workflow `.github/workflows/release.yml` compila a aplicação e o instalador no
Windows, confirma que a etiqueta corresponde à versão e publica a release com o
`TaskbarTempsSetup.exe` anexado.

---

## Utilização

| Ação | Resultado |
|---|---|
| Passar o cursor | Tooltip com CPU, RAM, GPU, disco e rede em detalhe |
| Clique simples | Janela **Sensores** com todos os valores e botão para copiar o relatório |
| Arrastar | Move o widget ao longo da barra de tarefas (a posição fica guardada) |
| Botão direito | Menu de contexto |

### Menu

- **Mostrar na barra de tarefas** — ocultar/mostrar o widget sem fechar a aplicação.
- **Ver sensores…** — janela de detalhes.
- **Procurar atualizações** — procura e instala uma versão mais recente.
- **Procurar atualizações automaticamente** — ativa/desativa a verificação periódica.
- **Atualizar a cada** — 0,5 s, 1 s, 2 s ou 5 s.
- **Tema** — Automático, Claro ou Escuro.
- **Mostrar uso quando não há sensor** — em vez de `--`, mostra a percentagem de
  utilização (útil para RAM DDR4, ver abaixo).
- **Iniciar com o Windows** — cria/remove a tarefa agendada.
- **Repor posição** — devolve o widget à posição predefinida.
- **Sair** — fecha a aplicação.

---

## Como são apresentados os valores

- `CPU 49° 12%` — temperatura e utilização do processador.
- `RAM 41° 51%` - temperatura e utilização da memória (a temperatura só aparece
  em módulos com sensor; sem sensor fica `RAM 51%`).
- `GPU 39° 8%` — temperatura e utilização da gráfica.
- `DISCO L1,2M E300K` — leitura (`L`) e escrita (`E`) em KB/s, MB/s ou GB/s.
- `REDE ▼1,2M ▲40K` — receção (`▼`) e envio (`▲`) em KB/s, MB/s ou GB/s
  (a letra `K`/`M`/`G` é a unidade por segundo).

Se faltar uma temperatura:

- **DDR5** (e DDR4 com sensor SPD/TSOD): aparecem a temperatura **e** a
  percentagem, por exemplo `RAM 41° 51%`.
- **DDR4 sem sensor**: não existe leitura possível por software. Com a opção
  *Mostrar uso quando não há sensor* ativa (predefinição), aparece a percentagem.
- **CPU sem PawnIO**: `CPU --` e o tooltip indica que falta o PawnIO.

### Tráfego de rede

As velocidades são lidas dos contadores de desempenho do Windows (incluem IPv4 e
IPv6) e somam as placas físicas ativas. Placas virtuais, VPN, Hyper-V, WSL e
semelhantes são ignoradas para não contar o mesmo tráfego duas vezes. Se os
contadores não estiverem disponíveis, é usada a pilha IPv4 do Windows.

O tooltip e a janela de sensores indicam o nome da placa quando existe apenas uma.

### Velocidade do disco

As velocidades de leitura e escrita vêm dos contadores de desempenho
`PhysicalDisk` (`_Total`), que somam o tráfego real de todos os discos. Se estes
não estiverem disponíveis, são usados os contadores `LogicalDisk` (`_Total`).
Os valores aparecem no widget como `DISCO L12M E3M` (leitura/escrita) e, em
detalhe, no tooltip e na janela de sensores.

---

## Arranque automático

A opção **Iniciar com o Windows** cria a tarefa agendada `TaskbarTemps` com:

- nível **HighestAvailable** (sem prompt UAC no arranque);
- execução com o token interativo do utilizador;
- `ExecutionTimeLimit = PT0S` (sem limite de tempo);
- diretório de trabalho igual ao da aplicação.

A tarefa é criada através de XML, para que caminhos com espaços funcionem
corretamente. Não é usada a chave `Run` do registo.

Também é possível gerir pela linha de comandos (com elevação):

```powershell
TaskbarTemps.exe --install-startup
TaskbarTemps.exe --remove-startup
```

---

## Desinstalação

**Aplicações instaladas → TaskbarTemps → Desinstalar** (ou
`"C:\Program Files\TaskbarTemps\TaskbarTemps.exe" --uninstall`). Remove a tarefa
agendada, o atalho, o registo de desinstalação e a pasta de instalação. Se quiser,
apague também `%APPDATA%\TaskbarTemps` e o PawnIO em *Aplicações instaladas*.

---

## Dados e registo

| Ficheiro | Conteúdo |
|---|---|
| `%APPDATA%\TaskbarTemps\settings.ini` | tema, intervalo, visibilidade, posição, opções |
| `%APPDATA%\TaskbarTemps\TaskbarTemps.log` | registo de arranque, erros e desinstalação |
| `%TEMP%\TaskbarTempsSetup.log` | registo da instalação silenciosa |

Apagar os ficheiros de definições repõe os valores predefinidos.

---

## Compilar

Não é necessário instalar o .NET SDK: os scripts usam o `csc.exe` do .NET Framework
incluído no Windows.

```powershell
.\build.ps1            # aplicação -> .\dist
.\build.ps1 -Clean     # limpa dist antes de compilar
.\build.ps1 -Run       # compila e inicia

.\build-setup.ps1      # instalador autónomo -> .\release\TaskbarTempsSetup.exe
```

Na primeira execução, o `build.ps1` descarrega:

- `LibreHardwareMonitorLib` 0.9.6 (e dependências) para `lib\extract`;
- `PawnIO_setup.exe` 2.2.0 para `lib` e copia-o para `dist`.

Sem rede, a compilação continua e o aviso indica que o instalador do PawnIO tem
de ser copiado manualmente para `dist`.

A build final usa `app.manifest` com `requireAdministrator` e DPI
**PerMonitorV2**. Para testes sem elevação existe `build.ps1 -NoAdminManifest`
(as temperaturas do CPU ficam indisponíveis).

O instalador incorpora os ficheiros de `dist` como recursos; não precisa de
ficheiros auxiliares ao lado do `.exe`.

---

## Limitações e resolução de problemas

- **CPU sempre `--`**: falta o PawnIO ou a aplicação não está elevada. O tooltip
  indica qual dos dois casos se aplica.
- **Widget não aparece**: a barra de tarefas está auto-oculta, na vertical ou
  coberta por uma janela em ecrã inteiro. Nestes casos o widget é escondido de
  propósito; ao voltar ao ambiente de trabalho reaparece.
- **Rede a `0`**: sem tráfego, ou os contadores de desempenho estão desativados
  (nesse caso é usada a contagem IPv4).
- **Disco `--`**: os contadores `PhysicalDisk`/`LogicalDisk` não estão
  disponíveis (por exemplo, contadores de desempenho desativados no sistema).
- **Não arranca com o Windows**: confirme a tarefa `TaskbarTemps` no Agendador de
  Tarefas e que o caminho do executável não foi alterado.
- **SmartScreen**: o instalador não está assinado, por isso o Windows pode mostrar
  *O Windows protegeu o seu PC*. Use **Mais informações → Executar mesmo assim**.
- **Smart App Control**: com o Smart App Control ativo, executáveis não assinados
  podem ser bloqueados. Assine o executável ou desative temporariamente o Smart
  App Control (*Segurança do Windows → Controlo de aplicações e browser*).
- Os sensores disponíveis dependem do hardware; não é possível garantir
  temperatura de RAM em todas as máquinas.

---

Desenvolvido por **[mamfo.net](https://www.mamfo.net)**.
