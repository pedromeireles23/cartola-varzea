#Requires -Version 5.1
<#
.SYNOPSIS
    Roda o E2E num banco proprio, sem sujar o banco de desenvolvimento.

.DESCRIPTION
    O E2E cria contas, organizacoes e convites ficticios a cada execucao. Este script
    aplica as migrations no banco Fut7Fantasy_E2E, do mesmo SQL Server do compose, e
    roda o Playwright com a API apontando para ele.

    A API precisa ser a que o Playwright sobe: com outra API ja escutando na porta
    5277, o Playwright a reaproveitaria, com o banco dela. Por isso o script para
    antes se a porta estiver ocupada. Rode o bootstrap.ps1 antes da primeira vez.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\infra\scripts\e2e-local.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\infra\scripts\e2e-local.ps1 -PlaywrightArgs 'specs/organization-team.spec.ts'
#>
[CmdletBinding()]
param(
    # Argumentos repassados ao `playwright test`, como um arquivo de spec.
    [string[]] $PlaywrightArgs = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositorio = (Resolve-Path (Join-Path (Join-Path $PSScriptRoot '..') '..')).Path
$arquivoEnv = Join-Path $repositorio '.env'
$projetoInfra = Join-Path $repositorio 'src\backend\src\Fut7Fantasy.Infrastructure'
$pastaE2E = Join-Path $repositorio 'tests\frontend'

if (-not (Test-Path $arquivoEnv)) {
    throw 'Arquivo .env nao encontrado. Rode infra\scripts\bootstrap.ps1 antes.'
}

$ocupada = Get-NetTCPConnection -LocalPort 5277 -State Listen -ErrorAction SilentlyContinue
if ($ocupada) {
    throw 'Ha uma API escutando na porta 5277. Pare-a para o Playwright subir a API do E2E.'
}

$linhaSenha = Get-Content $arquivoEnv | Where-Object { $_ -like 'MSSQL_SA_PASSWORD=*' } | Select-Object -First 1
$senha = $linhaSenha -replace '^MSSQL_SA_PASSWORD=', ''
$connectionString = "Server=127.0.0.1,1433;Database=Fut7Fantasy_E2E;User Id=sa;Password=$senha;TrustServerCertificate=True"

# A variavel de ambiente vence o user-secrets, entao a API do Playwright usa o banco do E2E.
$env:Database__ConnectionString = $connectionString
try {
    Write-Host '==> Aplicando as migrations no banco do E2E' -ForegroundColor Cyan
    dotnet ef database update --project $projetoInfra --startup-project $projetoInfra | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'dotnet ef database update falhou.' }

    Write-Host '==> Rodando o Playwright' -ForegroundColor Cyan
    Push-Location $pastaE2E
    try {
        npx.cmd playwright test @PlaywrightArgs
        $codigo = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
}
finally {
    Remove-Item Env:\Database__ConnectionString -ErrorAction SilentlyContinue
}

exit $codigo
