# Deploy — Sensatta Reports no servidor (Windows)

Passo a passo para publicar o serviço no servidor Windows e rodar como **Serviço
do Windows**. Você **publica no Ubuntu de dev** e **instala no servidor**.

---

## 0. Pré-requisitos

**No servidor (Windows):**
- Acesso **Administrador** (PowerShell elevado).
- O servidor **alcança o Oracle** (host:porta do banco).
- Porta **5080** liberável no firewall.
- Com publish `--self-contained true`, **não** precisa instalar .NET no servidor.

**No dev (Ubuntu):**
- `.NET SDK 10` (já tem).

---

## 1. Publicar (no Ubuntu de dev)

Gera a pasta `publish/` com o `.exe`, as DLLs, a pasta `reports/` e o
`update-service.ps1`:

```bash
cd ~/Desenvolvimento/report-sensatta-protheus
git pull origin main
dotnet publish src/SensattaReports.Api -c Release -r win-x64 --self-contained true -o publish
```

> `--self-contained true` embute o .NET (servidor não precisa de runtime). Pasta
> fica grande (~100 MB) — normal.

Compacte para levar ao servidor:
```bash
cd publish && zip -r ../sensatta-publish.zip . && cd ..
```

---

## 2. Levar ao servidor

Copie o `sensatta-publish.zip` para o servidor (pasta compartilhada, RDP, scp...)
e **extraia** em, por exemplo:

```
C:\Services\SensattaReports\
```

(Dentro deve ter `SensattaReports.Api.exe`, as DLLs, `reports\` e `update-service.ps1`.)

---

## 3. Configuração de produção (segredos)

Crie **`C:\Services\SensattaReports\appsettings.Local.json`** (fica **só no
servidor**, nunca no Git). Ajuste a connection string do Oracle e as origens do
painel:

```json
{
  "Data": { "Source": "Oracle" },
  "Oracle": { "ConnectionString": "User Id=USUARIO;Password=SENHA;Data Source=HOST:1521/SERVICO;" },
  "Jwt": {
    "Secret": "nM-bYYz3A-zhjtswgfa5ZEnSMCxpBPuySE3XgzVMFyu-7U4SxzqObY3wy198yjnI",
    "Issuer": "sensatta-reports",
    "Audience": "sensatta-reports",
    "ExpiresMinutes": 60,
    "Clients": [
      { "Id": "protheus", "Secret": "kHE4H476Uj3bThqr0ejMx-wN2lyLTsnL", "Role": "reports" },
      { "Id": "painel",   "Secret": "11Hq4G9quEctBNFCDwNLzHmFkjq9SfIJ", "Role": "admin" }
    ]
  },
  "Cors": { "Origins": [ "https://painel.betterbeef.com.br" ] },
  "Reports": { "Directory": "C:\\ProgramData\\SensattaReports\\reports" }
}
```

> O serviço roda como **Production** por padrão (não precisa setar ambiente).

---

## 4. Pasta persistente dos relatórios

Para os relatórios sobreviverem a uma atualização, aponte `Reports:Directory`
(acima) para uma pasta **fora** da pasta do serviço e copie a semente uma vez:

```bat
mkdir C:\ProgramData\SensattaReports\reports
xcopy /E /I C:\Services\SensattaReports\reports C:\ProgramData\SensattaReports\reports
```

---

## 5. Instalar o serviço

Num **PowerShell/CMD como Administrador**, na pasta do serviço:

```bat
cd C:\Services\SensattaReports
SensattaReports.Api.exe install
```

Cria o serviço `SensattaReports` (auto-start + auto-restart em falha) e inicia.

Controles:
```bat
SensattaReports.Api.exe stop
SensattaReports.Api.exe start
SensattaReports.Api.exe restart
SensattaReports.Api.exe uninstall
```

---

## 6. Firewall

```powershell
New-NetFirewallRule -DisplayName "Sensatta Reports 5080" -Direction Inbound -Protocol TCP -LocalPort 5080 -Action Allow
```

---

## 7. Validar

No navegador/máquina da rede (troque `<servidor>` pelo IP/nome):

```
http://<servidor>:5080/health      -> {"status":"ok",...}
http://<servidor>:5080/docs        -> página da API
```

Teste o token + um relatório:
```bash
# pega token do painel
curl -X POST http://<servidor>:5080/auth/token -H "Content-Type: application/json" ^
  -d "{\"clientId\":\"painel\",\"clientSecret\":\"11Hq4G9quEctBNFCDwNLzHmFkjq9SfIJ\"}"

# usa o access_token:
curl "http://<servidor>:5080/reports/romaneio-embarque-v2/pdf?romaneio_inicial=NNN&romaneio_final=NNN" ^
  -H "Authorization: Bearer <token>" -o teste.pdf
```

> Se algo falhar, veja o **Visualizador de Eventos do Windows** (Logs do Windows →
> Aplicativo) — o serviço registra os erros lá.

---

## 8. Entregar aos consumidores

- **Painel (Report Server):** URL `http://<servidor>:5080` + `clientId: painel` +
  o `clientSecret` de produção (envie em separado, não por e-mail aberto).
- **Protheus:** mesma URL + `clientId: protheus` + o `clientSecret` de produção.

---

## 9. Atualizações futuras (sem reinstalar)

No dev: `git pull` + `dotnet publish ...` (passo 1) → leve a nova `publish` ao
servidor (ex.: `C:\temp\publish-nova`). No servidor, como Administrador:

```powershell
cd C:\Services\SensattaReports
.\update-service.ps1 -Source "C:\temp\publish-nova"
```

Para o serviço, troca os binários (preservando `appsettings.Local.json` e
`reports`) e reinicia. **Não precisa reinstalar.**
