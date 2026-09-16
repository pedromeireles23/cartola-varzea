#Requires -Version 5.1
<#
.SYNOPSIS
    Prepara o ambiente de desenvolvimento local: containers, connection string e schema.

.DESCRIPTION
    Nao ha credencial fixa neste script. A senha do SQL Server vem do arquivo .env,
    que fica fora do Git; se o .env nao existir, o script gera uma senha aleatoria.
    Escrito para Windows PowerShell 5.1, o ambiente principal do projeto.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\infra\scripts\bootstrap.ps1
#>
[CmdletBinding()]
param(
    # Nao aplica as migrations, apenas sobe os containers e configura o segredo.
    [switch] $SkipMigrations
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositorio = (Resolve-Path (Join-Path (Join-Path $PSScriptRoot '..') '..')).Path
$arquivoEnv = Join-Path $repositorio '.env'
$projetoApi = Join-Path $repositorio 'src\backend\src\Fut7Fantasy.Api'
$projetoInfra = Join-Path $repositorio 'src\backend\src\Fut7Fantasy.Infrastructure'

function Write-Passo {
    param([string] $Mensagem)
    Write-Host "==> $Mensagem" -ForegroundColor Cyan
}

# 1. Senha local do SQL Server
if (-not (Test-Path $arquivoEnv)) {
    Write-Passo 'Criando .env com uma senha aleatoria para o SQL Server'
    $alfabeto = [char[]](([char]'a'..[char]'z') + ([char]'A'..[char]'Z') + ([char]'0'..[char]'9'))
    $aleatoria = -join (1..20 | ForEach-Object { $alfabeto | Get-Random })
    # Sem BOM: o docker compose nao reconhece a primeira chave de um .env com BOM.
    $semBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($arquivoEnv, "MSSQL_SA_PASSWORD=Dev!$aleatoria`n", $semBom)
}

$linhaSenha = Get-Content $arquivoEnv | Where-Object { $_ -like 'MSSQL_SA_PASSWORD=*' } | Select-Object -First 1
$senha = $linhaSenha -replace '^MSSQL_SA_PASSWORD=', ''
if ([string]::IsNullOrWhiteSpace($senha)) {
    throw 'MSSQL_SA_PASSWORD esta vazia no .env. Preencha antes de continuar.'
}

# 2. Dependencias em container
Write-Passo 'Subindo SQL Server, Azurite e Mailpit'
docker compose --project-directory $repositorio up -d
if ($LASTEXITCODE -ne 0) { throw 'docker compose up falhou. O Docker Desktop esta rodando?' }

Write-Passo 'Aguardando o SQL Server ficar saudavel'
$prazo = (Get-Date).AddMinutes(3)
do {
    $estado = docker inspect --format '{{.State.Health.Status}}' fut7fantasy-sqlserver 2>$null
    if ($estado -eq 'healthy') { break }
    if ((Get-Date) -gt $prazo) { throw "SQL Server nao ficou saudavel a tempo (ultimo estado: $estado)." }
    Start-Sleep -Seconds 5
} while ($true)

# 3. Connection string em user-secrets, nunca no repositorio
$connectionString = "Server=127.0.0.1,1433;Database=Fut7Fantasy;User Id=sa;Password=$senha;TrustServerCertificate=True"
Write-Passo 'Gravando a connection string em user-secrets'
dotnet user-secrets --project $projetoApi set 'Database:ConnectionString' $connectionString | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel gravar o user-secret.' }

# 4. Schema
if (-not $SkipMigrations) {
    Write-Passo 'Aplicando as migrations'
    $env:Database__ConnectionString = $connectionString
    try {
        dotnet ef database update --project $projetoInfra --startup-project $projetoInfra
        if ($LASTEXITCODE -ne 0) { throw 'dotnet ef database update falhou.' }
    }
    finally {
        Remove-Item Env:\Database__ConnectionString -ErrorAction SilentlyContinue
    }
}

Write-Passo 'Pronto'
Write-Host 'Suba a API com: dotnet run --project src\backend\src\Fut7Fantasy.Api'
Write-Host 'Mailpit: http://127.0.0.1:8025'
