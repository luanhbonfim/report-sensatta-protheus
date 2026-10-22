# Handoff — Painel do Sensatta Reports (para o Report Server)

> **Para quem é este documento:** o agente/dev que vai **reconstruir o painel** de
> relatórios dentro do outro sistema (o "Report Server"). O painel que existe hoje
> neste repositório (Blazor Server, em `/admin`) é só uma **referência**; a UI vai
> migrar para o seu sistema. **O backend não migra** — este serviço continua sendo
> a API + serviço Windows que gera os PDFs/Excel e guarda as definições dos
> relatórios. Você consome esta API por HTTP.

---

## 1. Visão geral

O serviço reproduz relatórios legados do **Sensatta** (templates Delphi/ReportBuilder
`.rtm`) e os entrega como **PDF** (layout fiel) ou **Excel** (dados crus). Cada
relatório é um **SQL** (com binds `:nome`) + um **template `.rtm`** + **parâmetros**
+ **ajustes de layout** opcionais. O ERP **Protheus** chama a API para gerar o PDF;
o **painel** serve para cadastrar/gerenciar os relatórios e ajustar o layout.

```
Protheus ──HTTP──▶  [ API + Serviço Windows ]  ──▶ PDF / Excel
                         │  (este repositório)
Painel  ──HTTP /api──────┘  (SEU sistema reconstrói a UI)
```

Dois grupos de endpoints:

| Grupo | Rota base | Quem chama | Auth |
|---|---|---|---|
| **Geração** | `/reports/...` | Protheus **e** o preview do painel | JWT (role qualquer) |
| **Gerenciamento** | `/api/...` | **o painel** | JWT (role `admin`) |

Base URL do serviço (dev): `http://localhost:5080`. Em produção, o host/porta do
serviço Windows.

---

## 2. Autenticação — JWT Bearer

**Todas** as chamadas (Protheus em `/reports/*` e painel em `/api/*`) levam um
**token JWT** no header:

```
Authorization: Bearer <token>
```

### Como obter o token (fluxo client-credentials)
Cada consumidor tem um **clientId + clientSecret** (combinados e configurados no
serviço em `Jwt:Clients`). Troca por um token em:

```
POST /auth/token
Content-Type: application/json
{ "clientId": "painel", "clientSecret": "<segredo do painel>" }
```
Resposta `200`:
```json
{ "access_token": "eyJhbGci...", "token_type": "Bearer", "expires_in": 3600, "role": "admin" }
```
`401` se as credenciais forem inválidas. O token expira em `expires_in` segundos
(renove pedindo outro). Dois clientes típicos:

| clientId | role | pode acessar |
|---|---|---|
| `protheus` | `reports` | `/reports/*` (gerar relatórios) |
| `painel` | `admin` | `/api/*` (gerenciar) **e** `/reports/*` |

### Preview em `<iframe>`
O navegador não manda header `Authorization` num `<iframe>`. Para o preview, passe o
token na **query string** das rotas **GET** de geração:
```
/reports/{codigo}/pdf?paramA=...&access_token=<token>
```
(só é aceito em `/reports/*`.)

### Observações
- **Dev:** se `Jwt:Secret` não estiver configurado no serviço, a proteção fica
  **desligada** e tudo responde sem token (facilita o desenvolvimento local).
- **Recomendado:** o **backend do seu painel** guarda o `clientSecret`, obtém o token
  e chama a API (server-to-server) — não exponha o secret no navegador. Para o
  preview, gere um token e injete no `src` do iframe.
- Sem CORS liberado por padrão (chamadas server-to-server). Se precisar chamar do
  browser, me avise que habilito CORS por origem.

Respostas são **JSON em camelCase**. Erros vêm como `{ "error": "mensagem" }` com o
status HTTP adequado (400/401/404/500).

---

## 3. Endpoints de GERAÇÃO (`/reports`) — não mudam

Usados pelo Protheus e pelo preview do painel.

### PDF (inline — abre/renderiza no navegador)
```
GET /reports/{codigo}/pdf?paramA=valor&paramB=valor
```
### Excel (download)
```
GET /reports/{codigo}/xlsx?paramA=valor&paramB=valor
```
### POST (alternativa com corpo JSON)
```
POST /reports/{codigo}
Content-Type: application/json
{ "format": "pdf", "parametros": { "romaneio_inicial": "22467778", "romaneio_final": "22467778" } }
```

- Parâmetros vão na **query string** (GET) ou em `parametros` (POST). Os nomes são
  os **binds** do SQL (ex.: `:romaneio_inicial` → `romaneio_inicial`).
- Datas aceitam ISO (`2026-10-01`) ou BR (`01/10/2026`).
- Resposta: o arquivo (PDF `Content-Type: application/pdf` inline; XLSX como
  download). Erros: 404 (código inexistente), 400 (parâmetro inválido/ausente),
  500 (erro de SQL/render — o corpo traz o detalhe).
- **No painel, o preview do PDF** é só um `<iframe src="/reports/{codigo}/pdf?...">`
  (funciona porque o PDF é servido *inline*).

---

## 4. Endpoints de GERENCIAMENTO (`/api`) — o painel consome

### 4.1 Catálogo (SIGMA.REPORT)
Lista os relatórios do catálogo mestre (Oracle `SIGMA.REPORT`) e marca os já importados.
```
GET /api/catalog
```
```json
[
  { "name": "ROMANEIO DE EMBARQUE V2", "codigo": "romaneio-embarque-v2", "imported": true },
  { "name": "RESUMO DE COLETAS - EUA", "codigo": "resumo-coletas-eua", "imported": true }
]
```
`codigo` é o *slug* do nome (minúsculo, sem acento, `[a-z0-9-]`) — é a chave usada
em todas as outras rotas e na URL de geração.

### 4.2 Relatórios importados
```
GET /api/reports
```
```json
[
  { "codigo": "romaneio-embarque-v2", "nome": "Romaneio de Embarque V2",
    "params": 2, "endpointPdf": "/reports/romaneio-embarque-v2/pdf",
    "endpointXlsx": "/reports/romaneio-embarque-v2/xlsx" }
]
```

### 4.3 Definição de um relatório (SQL + parâmetros)
```
GET /api/reports/{codigo}
```
```json
{
  "codigo": "romaneio-embarque-v2",
  "nome": "Romaneio de Embarque V2",
  "sql": "SELECT ... FROM ... WHERE ROMANEIO BETWEEN :romaneio_inicial AND :romaneio_final",
  "parametros": [
    { "name": "romaneio_inicial", "type": "Integer", "required": true, "label": "Romaneio inicial" },
    { "name": "romaneio_final",   "type": "Integer", "required": true, "label": "Romaneio final" }
  ]
}
```
`type` ∈ `String | Integer | Decimal | Date | DateTime`.

### 4.4 Salvar / importar um relatório
Cria (importa do catálogo) ou atualiza o SQL/parâmetros. O `.rtm` vem do catálogo
(na 1ª importação, pelo `nome`) ou é **mantido** (na atualização). O **SQL é você
que define** (campo de texto no painel).
```
POST /api/reports/{codigo}
Content-Type: application/json
{
  "nome": "Romaneio de Embarque V2",
  "sql": "SELECT ...",
  "parametros": [ { "name": "romaneio_inicial", "type": "Integer", "required": true, "label": "Romaneio inicial" } ]
}
```
Resposta `200`: `{ "codigo": "...", "nome": "...", "endpointPdf": "/reports/.../pdf" }`.
Erros: `400` se faltar SQL ou se não houver `.rtm` no catálogo nem salvo.

> **Detecção de parâmetros:** os binds do SQL são `:nome` (regex `(?<!:):(\w+)`).
> Pode detectá-los no front e pré-preencher a tabela de parâmetros (tipo "adivinhado"
> por heurística do nome: `data*`→Date, `*_id`/`cod*`→Integer, etc.).

### 4.5 Layout de um relatório (para o editor visual)
Retorna as **bandas** e **campos** do `.rtm` (posições em **mm**) + os **ajustes**
salvos. É o que alimenta o editor de arrastar.
```
GET /api/reports/{codigo}/layout
```
```json
{
  "pageWidthMm": 210, "pageHeightMm": 297, "contentWidthMm": 197.3,
  "marginLeftMm": 6.35, "marginTopMm": 6.35,
  "bands": [
    {
      "kind": "Header", "label": "Cabeçalho da página", "heightMm": 9.79,
      "elements": [
        { "name": "ppDBText4", "type": "DbText", "leftMm": 42.33, "topMm": 6.08,
          "widthMm": 11.64, "heightMm": 2.91, "text": null, "field": "P_ROMANEIO_INICIAL", "varType": null },
        { "name": "ppLabel39", "type": "Label", "leftMm": 28.05, "topMm": 6.08,
          "widthMm": 13.76, "heightMm": 2.91, "text": "ROMANEIO:", "field": null, "varType": null }
      ]
    }
  ],
  "overrides": {
    "ppDBText5": { "leftMm": 57.15, "topMm": 6.08, "widthMm": null, "heightMm": null, "hidden": false }
  }
}
```
- `type` ∈ `Label | DbText | DbMemo | SystemVariable | Line | Shape | Image`.
- `name` (UserName do `.rtm`) é a **chave** dos ajustes.
- Bandas na ordem: Título, Cabeçalho, (cab. de cada grupo), Detalhe, (rodapé de
  cada grupo), Rodapé, Resumo.

### 4.6 Salvar os ajustes de layout
```
PUT /api/reports/{codigo}/layout
Content-Type: application/json
{
  "overrides": {
    "ppDBText5": { "leftMm": 57.15, "topMm": 6.08 },
    "ppLabel5":  { "hidden": true }
  }
}
```
- Cada medida (`leftMm`, `topMm`, `widthMm`, `heightMm`, `hidden`) é **opcional**;
  o que for `null`/omitido mantém o valor do `.rtm`.
- Substitui o conjunto inteiro de ajustes daquele relatório. Resposta `200`:
  `{ "codigo": "...", "campos": 2 }`.

### 4.7 Log das chamadas do Protheus
```
GET /api/calls?take=200
```
```json
{
  "total": 134, "hoje": 20, "erros": 2,
  "recent": [
    { "when": "2026-10-22T17:40:26", "codigo": "romaneio-embarque-v2", "format": "Pdf",
      "ok": true, "status": 200, "ms": 812, "query": "romaneio_inicial=22467778&romaneio_final=22467778", "error": null }
  ]
}
```
(É um buffer em memória das últimas ~500 chamadas; reinicia com o serviço.)

---

## 5. Telas a reconstruir no painel

> Visual de referência: identidade do **itms** (Bootstrap 5.3, Montserrat, sidebar
> vermelho Better Beef — primário `#7f1810`, acento `#d20a11`). Adapte ao seu
> design system.

### 5.1 Dashboard
Cartões de indicadores (relatórios ativos = `GET /api/reports`.length; chamadas hoje /
total / erros = `GET /api/calls`) + tabela das últimas chamadas. Atualiza a cada ~5s.

### 5.2 Relatórios
Tabela de `GET /api/reports`: nome, código, nº de params, **endpoint** (`/reports/{cod}/pdf`,
mostrar como link/código copiável), e ações: **Ver PDF** (abre o endpoint), **SQL**
(editar SQL/parâmetros), **Layout** (editor visual).

### 5.3 Importar
Lista `GET /api/catalog` com busca/filtro; cada item vira um card (badge "ativo" se
`imported`). Clicar abre o editor de **SQL + parâmetros**; salvar = `POST /api/reports/{codigo}`.

### 5.4 Editor de SQL + parâmetros
- Campo **código** (slug; na importação vem do catálogo).
- **Textarea de SQL** (grande; é um SELECT com binds `:nome`).
- Botão **Detectar** parâmetros (regex dos binds) + tabela editável de parâmetros
  (name, type[select], required[check], label).
- **Salvar** → `POST /api/reports/{codigo}`. Depois mostre o **endpoint** e um
  mini-testador (inputs dos params → link/preview do PDF).

### 5.5 Chamadas
Tabela de `GET /api/calls` (quando, relatório, formato, status, tempo, parâmetros).
Auto-refresh.

### 5.6 Editor de LAYOUT (o mais importante — detalhado na seção 6)

---

## 6. Especificação do Editor de Layout

Objetivo: ajustar a posição/tamanho dos campos do `.rtm` **sem alterar o `.rtm`**,
gravando um *overlay* (os `overrides`). O serviço aplica esses ajustes ao renderizar.

### 6.1 Dados
- Carregar: `GET /api/reports/{codigo}/layout` (bandas, campos em mm, overrides atuais).
- Salvar: `PUT /api/reports/{codigo}/layout` com `{ overrides }`.

### 6.2 Canvas
- Converta **mm → px** por uma **escala** (px por mm). Comece **ajustando à largura**:
  `escala = (larguraÚtilDoCanvas − ~56) / contentWidthMm`; ofereça **zoom** (−/+),
  limitando a escala (ex.: 2.5 a 14).
- Desenhe **cada banda** como um bloco com título (`label` + `heightMm`), largura =
  `contentWidthMm × escala`, altura = `heightMm × escala`, `position: relative`.
- Dentro da banda, cada **campo** é um `position: absolute` em
  `left = leftMm×escala`, `top = topMm×escala`, `width/height` idem. Rótulo do box:
  `Label`→texto; `DbText`/`DbMemo`→nome do campo; `SystemVariable`→tipo; `Line`/`Shape`/`Image`→ícone.
- **Posição efetiva** de um campo = override (se houver, por `name`) senão o valor do `.rtm`:
  `left = override.leftMm ?? el.leftMm` (idem top/width/height); `hidden = override.hidden`.

### 6.3 Interações
- **Arrastar** um campo move-o dentro da banda → atualiza `leftMm/topMm` do override
  (em mm = px / escala). Faça o arraste no **cliente** (suave) e só persista no estado
  ao soltar.
- **Selecionar** um campo abre um painel com **left/top/width/height (mm)** editáveis
  e um **toggle "ocultar"** (`hidden`).
- **Desfazer** (pilha de snapshots dos overrides), **limpar tudo**, **editar o JSON**
  dos overrides direto (um textarea que valida e aplica), **voltar um campo ao original**
  (remover a entrada do override).
- **Salvar** → `PUT .../layout`. **Pré-visualizar** → modal grande com
  `<iframe src="/reports/{codigo}/pdf?params&access_token=<token>&v=timestamp">` (o
  `access_token` autentica o iframe; o `v` evita cache). O preview reflete o que
  está **salvo** — salve antes.

### 6.4 Formato dos overrides (o corpo do PUT e o `report.layout.json`)
```json
{
  "ppDBText5": { "leftMm": 57.15, "topMm": 6.08, "widthMm": null, "heightMm": null, "hidden": false },
  "ppLabel5":  { "hidden": true }
}
```
Chave = `name` do campo. Medidas em mm, todas opcionais.

---

## 7. O que fica de cada lado

| Fica na **API** (este repositório) | Vai para o **painel** (seu sistema) |
|---|---|
| Geração PDF/Excel a partir do `.rtm`+SQL+dados | Todas as telas/UX |
| Parser do `.rtm`, renderizador, aplicação dos overrides | Editor visual de layout (desenho + arraste) |
| Catálogo (SIGMA.REPORT), armazenamento dos relatórios (arquivos) | Autenticação/sessão do painel (seu login) |
| Log de chamadas | Dashboard/consumo dos indicadores |
| API `/api/*` + geração `/reports/*` | — |

O painel **não** mexe em arquivos diretamente — faz tudo pela API `/api/*`.

---

## 8. Notas

- **Concorrência / performance**: a API limita relatórios pesados simultâneos
  (semáforo); o painel não precisa se preocupar.
- **Logo e fontes** do PDF são resolvidas no serviço.
- **Segurança**: tudo usa **JWT Bearer** (seção 2). `/reports/*` aceita qualquer
  token válido (role `reports` ou `admin`); `/api/*` exige role `admin`. Em dev,
  sem `Jwt:Secret`, fica aberto.
- **Migração**: enquanto o painel novo não entra, o painel Blazor em `/admin` segue
  funcionando como referência. Quando o seu painel assumir, o Blazor (e a auth por
  cookie) pode ser removido do serviço para ele virar 100% headless.

Dúvidas sobre qualquer endpoint ou comportamento: o código de referência está em
`src/SensattaReports.Api/ManagementApi.cs` (API) e
`src/SensattaReports.Api/Components/Pages/LayoutEditor.razor` (editor).
