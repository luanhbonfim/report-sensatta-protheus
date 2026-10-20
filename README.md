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
| `GET` | `/health` | status do serviço |
| `GET` | `/reports/{codigo}/pdf?param=valor` | PDF |
| `GET` | `/reports/{codigo}/xlsx?param=valor` | XLSX |
| `POST` | `/reports/{codigo}` | PDF/XLSX (corpo JSON) |

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

Os reports ficam **versionados no Git**, como arquivos na pasta `reports/`. Para
cada report com código `x`, crie três arquivos:

- `reports/x.sql` — o SELECT, com binds `:param`
- `reports/x.rtm` — o template do ReportBuilder
- `reports/x.params.json` — os parâmetros (opcional)

Passos:

1. No Sensatta, pegue o **SQL** e o **`.rtm`** do report e salve em `reports/`.
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

## Deploy como serviço

- **Windows Server (Windows Service):**
  ```bash
  dotnet publish src/SensattaReports.Api -c Release -r win-x64 --self-contained false
  # no servidor:
  sc.exe create SensattaReports binPath= "C:\caminho\SensattaReports.Api.exe"
  sc.exe start SensattaReports
  ```
- **Linux (systemd):** publique com `-r linux-x64` e crie um unit file apontando
  para o executável. O serviço já chama `UseSystemd()`.

Por padrão escuta em `http://0.0.0.0:5080` (ajuste em `Urls`). Para TLS, use um
proxy reverso (IIS/nginx) na frente.

---

## Componentes já suportados do `.rtm`

`TppLabel`, `TppDBText`, `TppLine`, `TppShape`, `TppImage`, bandas de cabeçalho,
detalhe, rodapé e grupo (cabeçalho/rodapé). Outros componentes (totais,
subrelatórios, código de barras) podem ser adicionados conforme a necessidade.
