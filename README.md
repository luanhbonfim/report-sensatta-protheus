# Sensatta Reports → Protheus

Serviço em **.NET 10** que reproduz os relatórios do sistema legado **Sensatta**
(feitos em **ReportBuilder/Delphi**, os arquivos `.rtm`) e os entrega como
**PDF** (fiel ao layout) ou **Excel** (dado cru), via uma **API HTTP** que o
Protheus chama quando precisa.

A ideia: o report tem um **SQL** (de onde vêm os dados) e um **template `.rtm`**
(como desenhar). O serviço lê os dois, roda o SQL no Oracle com os parâmetros
recebidos e gera o arquivo.

---

## Arquitetura

```
Protheus ──HTTP──▶  API  ──▶ ReportService ──▶ Oracle (roda o SQL)
                                    │
                                    └────────▶ Renderizador ──▶ PDF / XLSX
```

Projetos (em `src/`):

| Projeto | Responsabilidade |
|---|---|
| **Core** | Modelos e interfaces (contratos). Não depende de nada. |
| **ReportBuilder** | Lê o `.rtm` (parser DFM) e gera PDF (PdfSharp) e Excel (ClosedXML). |
| **Data** | Acesso ao Oracle (roda o SQL, lê a tabela `REPORT`). |
| **Api** | O serviço HTTP que o Protheus chama. Junta tudo. |
| **tests/** | Testes do parser contra o `.rtm` de exemplo. |

Dependências: `Api → (Core, ReportBuilder, Data)`, `ReportBuilder → Core`, `Data → Core`.

---

## Pré-requisitos

- **.NET 10 SDK** (`dotnet --version` → `10.0.x`)
- No Linux, para o PDF sair com a fonte certa:
  ```bash
  sudo apt install fonts-liberation        # Liberation Sans (compatível com Arial)
  # ou, para Arial "de verdade":
  sudo apt install ttf-mscorefonts-installer
  ```

---

## Rodar em desenvolvimento (sem Oracle)

Já vem configurado em modo **dev**: repositório lê os arquivos da pasta `reports/`
e os dados vêm de uma fonte **Demo** (linhas fixas). Não precisa de banco.

```bash
dotnet build
dotnet run --project src/SensattaReports.Api
```

Com o serviço no ar (porta **5080**), gere o report de exemplo:

```bash
# PDF (layout do .rtm)
curl -o resumo.pdf  "http://localhost:5080/reports/resumo-coletas-eua/pdf?data_ini=2026-10-01&data_fim=2026-10-31"

# Excel (dado cru)
curl -o resumo.xlsx "http://localhost:5080/reports/resumo-coletas-eua/xlsx?data_ini=2026-10-01&data_fim=2026-10-31"
```

Abra o `resumo.pdf` — deve sair com cabeçalho, barra de grupo por produto e as linhas.

### Testes

```bash
dotnet test
```

---

## Rodar em produção (com Oracle)

Ative o modo Oracle por variável de ambiente (ou `appsettings.Local.json`):

```bash
export ASPNETCORE_ENVIRONMENT=Production
export Oracle__ConnectionString="User Id=RELATORIOS;Password=SENHA;Data Source=HOST:1521/SERVICO;"
dotnet run --project src/SensattaReports.Api
```

> **Nunca** comite senha no Git. Use a variável `Oracle__ConnectionString` ou o
> arquivo `appsettings.Local.json` (está no `.gitignore`).

No modo Production (`appsettings.Production.json`): `Reports:Source=Oracle` e
`Data:Source=Oracle`.

### Tabela de configuração dos reports

Crie num schema **seu** (não no do Sensatta):

```sql
CREATE TABLE REPORT (
  CODIGO        VARCHAR2(100) PRIMARY KEY,   -- ex.: resumo-coletas-eua
  NOME          VARCHAR2(200) NOT NULL,
  SQL_TEXT      CLOB          NOT NULL,       -- o SELECT com binds :data_ini, :data_fim
  TEMPLATE_RTM  CLOB          NOT NULL,       -- o conteudo do .rtm
  PARAMETROS    CLOB                          -- JSON dos parametros
);
```

`PARAMETROS` é um JSON como:

```json
[
  { "name": "data_ini", "type": "Date", "required": true, "label": "Data inicial" },
  { "name": "data_fim", "type": "Date", "required": true, "label": "Data final" }
]
```

Tipos aceitos: `String`, `Integer`, `Decimal`, `Date`, `DateTime`.

O conexão usa o **Oracle.ManagedDataAccess.Core** (driver 100% gerenciado):
conecta no Oracle 11g **sem** precisar instalar Oracle Client.

---

## Endpoints (integração Protheus)

| Método | Rota | Retorno |
|---|---|---|
| `GET` | `/health` | status do serviço (público) |
| `POST` | `/auth/token` | emite o token JWT (público) |
| `GET` | `/reports/{codigo}/pdf?param=valor` | PDF |
| `GET` | `/reports/{codigo}/xlsx?param=valor` | XLSX |
| `POST` | `/reports/{codigo}` | PDF/XLSX (corpo JSON) |

As rotas de **gerenciamento** `/api/*` (catálogo, CRUD de reports, layout, log)
são consumidas pelo painel externo — ver **[`docs/HANDOFF-PAINEL.md`](docs/HANDOFF-PAINEL.md)**.
Com JWT ligado, as chamadas levam `Authorization: Bearer <token>`.

Corpo do POST:

```json
{ "format": "pdf", "parametros": { "data_ini": "2026-10-01", "data_fim": "2026-10-31" } }
```

Datas aceitam ISO (`2026-10-01`) ou BR (`01/10/2026`).

Erros: `404` report não encontrado, `400` parâmetro obrigatório ausente/ inválido.

> **Segurança**: só os parâmetros **declarados** no report viram bind no Oracle,
> e sempre por *bind* (`:nome`) — nunca concatenados. Isso bloqueia SQL injection.

---

## Como cadastrar um novo report

Os reports ficam **versionados no Git**, cada um na sua **subpasta** em `reports/`:

```
reports/
├── _assets/
│   └── logo.png                 # logo generica, usada em todos os reports
└── <codigo>/
    ├── report.sql               # o SELECT, com binds :param
    ├── report.rtm               # o template do ReportBuilder
    └── report.params.json       # os parametros (opcional)
```

A **logo** (`reports/_assets/logo.png`) é desenhada na posição que cada `.rtm`
define para a imagem — assim fica sempre no mesmo canto, em todos os reports, sem
depender da imagem embutida no template. Para trocar a logo, basta substituir
esse PNG.

Passos para um report novo:

1. No Sensatta, pegue o **SQL** e o **`.rtm`** e salve em `reports/<codigo>/`
   como `report.sql` e `report.rtm`.
2. **Importante — ordenação dos grupos**: o motor agrupa por quebra sequencial,
   então o `ORDER BY` do SQL **precisa começar pelos campos de grupo do `.rtm`**,
   na ordem dos grupos. Ex.: se o `.rtm` agrupa por `CODIGO_PRODUTO` e depois
   `LOTE_EUA`, o SQL deve terminar com
   `ORDER BY CODIGO_PRODUTO, LOTE_EUA, ...`.
3. Commit + push. Fica disponível em `/reports/{codigo}/pdf`. **Sem recompilar** a
   lógica — só os arquivos são copiados no build.

> A pasta `reports/` é copiada para a saída do build/publish, então acompanha o
> serviço onde ele rodar. Em produção, o **Oracle** serve só os dados (roda o SQL);
> as definições continuam vindo dos arquivos.
>
> Alternativa: existe também um `OracleReportRepository` caso um dia você prefira
> guardar as definições numa tabela `REPORT` no banco (basta trocar
> `Reports:Source` para `Oracle`).

---

## API de gerenciamento + autenticação

Este serviço é **headless** (só API) — o painel vive em outro sistema (o *Report
Server*) e consome a API de gerenciamento `/api/*` (catálogo, CRUD de reports,
layout para o editor visual, log de chamadas). O contrato completo e a
especificação das telas estão em **[`docs/HANDOFF-PAINEL.md`](docs/HANDOFF-PAINEL.md)**.

**Autenticação (JWT Bearer):** tudo (`/reports/*` e `/api/*`) usa token. O
consumidor pega o token em `POST /auth/token` (clientId+clientSecret de
`Jwt:Clients`) e manda `Authorization: Bearer <token>`. `/reports/*` aceita
qualquer role; `/api/*` exige role `admin`. **Sem `Jwt:Secret` configurado, a
auth fica desligada (dev).** Detalhes no handoff e em `src/SensattaReports.Api/Auth.cs`.

---

## Deploy como serviço (Windows Server)

### 1. Publicar (pode ser do próprio Ubuntu)

Gera os binários Windows numa pasta `publish/`. `--self-contained true` embute o
.NET (não precisa instalar runtime no servidor):

```bash
dotnet publish src/SensattaReports.Api -c Release -r win-x64 --self-contained true -o publish
```

> Alternativa menor: `--self-contained false` — aí o servidor precisa ter o
> **ASP.NET Core Runtime 10** instalado.

### 2. Copiar para o servidor

Copie a pasta `publish/` para, por exemplo, `C:\Services\SensattaReports`.

### 3. Configurar (no servidor, ao lado do .exe)

Crie `C:\Services\SensattaReports\appsettings.Local.json` (não vai para o Git):

```json
{
  "Data": { "Source": "Oracle" },
  "Oracle": { "ConnectionString": "User Id=...;Password=...;Data Source=HOST:1521/SERVICO;" },
  "Jwt": {
    "Secret": "um-segredo-forte-32+-chars",
    "Clients": [
      { "Id": "protheus", "Secret": "...", "Role": "reports" },
      { "Id": "painel",   "Secret": "...", "Role": "admin" }
    ]
  },
  "Cors": { "Origins": [ "https://painel.betterbeef.com.br" ] },
  "Reports": { "Directory": "C:\\ProgramData\\SensattaReports\\reports" }
}
```

Crie a pasta persistente dos reports e copie a semente uma vez:

```bat
mkdir C:\ProgramData\SensattaReports\reports
xcopy /E /I C:\Services\SensattaReports\reports C:\ProgramData\SensattaReports\reports
```

(Assim os reports importados pela API sobrevivem a um deploy novo.)

### 4. Instalar como Serviço do Windows

Num PowerShell/CMD **como administrador** (atenção ao espaço depois de `binPath=`):

```bat
sc.exe create SensattaReports binPath= "C:\Services\SensattaReports\SensattaReports.Api.exe" start= auto
sc.exe description SensattaReports "Servico de relatorios Sensatta -> Protheus"
sc.exe start SensattaReports
```

Testar: `http://<servidor>:5080/health` deve responder `ok`.

### 5. Firewall e rede

- Libere a porta **5080** para o Protheus e o painel alcançarem o serviço.
- Ative o **JWT** (`Jwt:Secret` + `Jwt:Clients`) em produção. Para TLS, use
  IIS/nginx como proxy reverso na frente.

### Atualizar (novo deploy)

```bat
sc.exe stop SensattaReports
rem copie os novos arquivos da pasta publish por cima (menos o appsettings.Local.json)
sc.exe start SensattaReports
```

### Linux (systemd), se um dia precisar

Publique com `-r linux-x64` e crie um unit file apontando para o executável —
o serviço já chama `UseSystemd()`.

Por padrão escuta em `http://0.0.0.0:5080` (ajuste em `Urls`).

---

## Componentes já suportados do `.rtm`

- **Elementos:** `TppLabel`, `TppDBText`, `TppDBMemo` (texto multi-linha que
  quebra e flui entre páginas), `TppSystemVariable` (data/hora e número de
  página — `vtDateTime`, `vtDate`, `vtTime`, `vtPageNo`, `vtPageCount`,
  `vtPageSetDesc`), `TppLine`, `TppShape`, `TppImage`.
- **Bandas:** cabeçalho, detalhe, rodapé (fixado no pé de cada página, com o
  número de página calculado em segunda passada), resumo e **vários grupos
  aninhados** com cabeçalho e **rodapé de grupo (subtotais)**.
- **Grupos:** quebra por campo (`BreakName`), `NewPage` (quebra de página a cada
  troca do grupo) e bandas `phDynamic` (altura acompanha o conteúdo).

Outros componentes (subrelatórios, código de barras) podem ser adicionados
conforme a necessidade.

### Exemplos incluídos

| Código | Nome | Características |
|---|---|---|
| `resumo-coletas-eua` | Resumo de Coletas EUA | A4 paisagem, 1 grupo, logo |
| `romaneio-embarque-v2` | Romaneio de Embarque V2 | A4 retrato, 3 grupos aninhados, rodapés com subtotais, memo e número de página |

> **Nota sobre o `romaneio-embarque-v2`:** o `.rtm` usa um único *pipeline*
> (`rbpl_relatorio`); os subtotais (`TOTAL_PEDIDO_*`, `QTDE_COMPOSICAO`, ...) são
> colunas do próprio `SELECT` (funções de janela), desenhadas nos rodapés de
> grupo. O memo `TST` (lista de itens/peças da composição) é uma coluna de texto
> que precisa ser montada no SQL; enquanto ela não existir no retorno, o memo
> aparece vazio — o resto do layout é fiel.
