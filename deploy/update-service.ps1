# Atualiza os binarios do servico SEM reinstalar.
# Para o servico, troca os arquivos pela nova versao e inicia de novo.
#
# Uso (PowerShell como Administrador, na pasta do servico):
#   .\update-service.ps1 -Source "C:\caminho\da\nova\publish"
#
# - Preserva o appsettings.Local.json (segredos) e a pasta reports local.
# - Nao precisa recriar o servico: ele continua registrado apontando pro exe.

param(
    [Parameter(Mandatory = $true)][string]$Source,   # pasta da nova publish
    [string]$Name = "SensattaReports",
    [string]$Dest = $PSScriptRoot                     # pasta do servico (onde esta este script/o exe)
)

if (-not (Test-Path (Join-Path $Source "SensattaReports.Api.exe"))) {
    Write-Error "Nao achei SensattaReports.Api.exe em '$Source'."
    exit 1
}

Write-Host "Parando o servico '$Name'..."
sc.exe stop $Name | Out-Null
Start-Sleep -Seconds 5   # espera liberar o .exe

Write-Host "Copiando a nova versao ($Source -> $Dest)..."
# /E = subpastas; /XF preserva o Local.json; /XD preserva a pasta reports local
robocopy $Source $Dest /E /XF appsettings.Local.json /XD reports | Out-Null

Write-Host "Iniciando o servico..."
sc.exe start $Name

Write-Host "Pronto. Confira: http://localhost:5080/health"
