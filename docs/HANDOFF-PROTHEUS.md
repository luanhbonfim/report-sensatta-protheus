# Handoff — Protheus → Serviço de Relatórios (Sensatta Reports)

> **Para quem é:** o analista que vai fazer o **Protheus (ADVPL)** chamar o serviço
> para **gerar os relatórios** (PDF/Excel). O serviço já existe e está pronto — você
> só consome a API por HTTP. Este documento é o contrato do lado do Protheus.

---

## 1. Visão geral

O serviço reproduz os relatórios legados do **Sensatta** (layout `.rtm`) e devolve
**PDF** (fiel ao layout) ou **Excel** (dados crus). O Protheus chama um endpoint
HTTP passando os **parâmetros** (ex.: romaneio inicial/final) e recebe o arquivo.

```
Protheus ──HTTP (com token)──▶ Serviço ──▶ PDF / Excel
```

Toda chamada é autenticada por **JWT**: o Protheus primeiro pega um **token**
(válido 1h) e o envia em cada requisição.

---

## 2. Dados de conexão

| Item | Valor |
|---|---|
| **URL base (dev)** | `http://192.168.85.61:5080` |
| **URL base (produção)** | *(host/porta do serviço Windows — a combinar)* |
| **clientId** | `protheus` |
| **clientSecret** | *(a equipe do serviço envia em separado — NÃO fica neste documento)* |

> A porta padrão é **5080**. Se não conectar, confirmar liberação de firewall.

> 📖 **Página interativa da API:** abra **`{BASE}/docs`** no navegador para ver e
> testar todos os endpoints (ex.: `http://192.168.85.61:5080/docs`).

---

## 3. Autenticação (pegar o token)

Antes de gerar relatório, obtenha um token:

```
POST {BASE}/auth/token
Content-Type: application/json

{ "clientId": "protheus", "clientSecret": "<o segredo recebido>" }
```

Resposta `200`:
```json
{ "access_token": "eyJhbGci...", "token_type": "Bearer", "expires_in": 3600, "role": "reports" }
```

- O `access_token` vale **1 hora** (`expires_in` em segundos).
- Guarde o token em memória e **reutilize** nas chamadas durante ~1h.
- Quando expirar (ou tomar **401**), **peça outro** token (mesmo POST). Não existe
  "refresh" — é só chamar o `/auth/token` de novo com o `clientSecret`.
- Credenciais inválidas → `401`.

---

## 4. Gerar o relatório

Envie o token no header **`Authorization: Bearer <token>`**.

### Opção A — GET (mais simples)
```
GET {BASE}/reports/{codigo}/pdf?param1=valor&param2=valor
GET {BASE}/reports/{codigo}/xlsx?param1=valor&param2=valor
Authorization: Bearer <token>
```

### Opção B — POST (parâmetros no corpo)
```
POST {BASE}/reports/{codigo}
Authorization: Bearer <token>
Content-Type: application/json

{ "format": "pdf", "parametros": { "romaneio_inicial": "22467778", "romaneio_final": "22467778" } }
```

### Regras
- **`codigo`** = o código do relatório (ex.: `romaneio-embarque-v2`). A equipe do
  serviço informa o código e os parâmetros de cada relatório.
- **Parâmetros**: os nomes são os do relatório (ex.: `romaneio_inicial`,
  `romaneio_final`, ou `data_ini`/`data_fim`). Datas aceitam `AAAA-MM-DD`
  (`2026-10-31`) ou `DD/MM/AAAA` (`31/10/2026`).
- **Resposta OK (`200`)**: o conteúdo é o **arquivo** (bytes do PDF/XLSX). O
  `Content-Type` é `application/pdf` ou o do Excel. Grave os bytes num arquivo.
- **Erros**: `401` sem token/expirado, `403` token sem permissão, `404` código
  inexistente, `400` parâmetro obrigatório ausente/ inválido, `500` erro ao gerar
  (o corpo traz o detalhe).

---

## 5. Exemplo em ADVPL

> Esqueleto usando `FWRest` + `JsonObject`. Ajuste à sua versão do Protheus e à
> forma de gravar/Еnviar o arquivo (impressão, spool, e-mail, etc.).

```advpl
#include "totvs.ch"

Static cBaseUrl  := "http://192.168.85.61:5080"
Static cClientId := "protheus"
Static cSecret   := "<clientSecret recebido da equipe do servico>"

// Gera o Romaneio de Embarque e grava o PDF em disco
User Function GeraRomaneio(nRomIni, nRomFim)
    Local cToken := U_RepToken()
    Local oRest  := FWRest():New(cBaseUrl)
    Local aHeader := {}
    Local cQuery := "romaneio_inicial=" + cValToChar(nRomIni) + ;
                    "&romaneio_final="  + cValToChar(nRomFim)
    Local cPdf   := ""

    If Empty(cToken)
        MsgStop("Nao foi possivel autenticar no servico de relatorios.")
        Return
    EndIf

    aAdd(aHeader, "Authorization: Bearer " + cToken)

    oRest:setPath("/reports/romaneio-embarque-v2/pdf?" + cQuery)
    If oRest:Get(aHeader)
        cPdf := oRest:GetResult()          // bytes do PDF
        MemoWrite("C:\temp\romaneio.pdf", cPdf)
    Else
        MsgStop("Erro ao gerar: " + oRest:GetLastError())
    EndIf
Return

// Pega o token (client-credentials). Idealmente cacheie por ~1h.
User Function RepToken()
    Local oRest   := FWRest():New(cBaseUrl)
    Local aHeader := {"Content-Type: application/json"}
    Local cBody   := '{"clientId":"' + cClientId + '","clientSecret":"' + cSecret + '"}'
    Local cToken  := ""
    Local oJson

    oRest:setPath("/auth/token")
    oRest:SetPostParams(cBody)
    If oRest:Post(aHeader)
        oJson := JsonObject():New()
        If Empty(oJson:FromJson(oRest:GetResult()))
            cToken := oJson["access_token"]
        EndIf
    EndIf
Return cToken
```

---

## 6. Boas práticas

- **Cacheie o token** (variável estática / tabela temporária) e reaproveite por ~1h;
  não peça um token novo a cada relatório.
- **Trate o 401**: se vier 401, peça um token novo e refaça a chamada **uma vez**.
- **Timeout**: relatórios grandes podem levar alguns segundos — use um timeout
  folgado (ex.: 60s).
- **Erros**: logue o `codigo` + parâmetros + status para facilitar o diagnóstico com
  a equipe do serviço.

---

Dúvidas sobre **códigos de relatório e parâmetros** → equipe do serviço.
Contrato técnico completo da API → `docs/HANDOFF-PAINEL.md` (seção 2 e 3) no repositório do serviço.
