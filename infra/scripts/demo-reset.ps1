#Requires -Version 5.1
<#
.SYNOPSIS
    Recria o ambiente de demonstracao local do zero: banco, contas e a historia da Copa.

.DESCRIPTION
    Apaga o banco Fut7Fantasy_Demo do SQL Server do compose, recria pelas migrations e
    roda o seed (src/backend/src/Fut7Fantasy.Demo), que conta tres semanas de campeonato
    pelas regras da aplicacao. Rodar de novo deixa o mesmo estado.

    As senhas das contas demo nao estao no codigo: na primeira vez o script gera tres
    senhas aleatorias e guarda no .env, que fica fora do Git. Nas seguintes, reaproveita.
    Nenhuma senha e impressa; para ver, abra o .env.

    Depois do reset, suba a API apontando para o banco da demo, com a entrada de
    visitante ligada (o botao "Entrar como visitante" na tela de entrada):
      $env:Database__ConnectionString = '<a mesma connection string com Database=Fut7Fantasy_Demo>'
      $env:DemoAccess__Enabled = 'true'
      $env:DemoAccess__ViewerEmail = 'visitante@demo.cartola-varzea.test'
      dotnet run --project src\backend\src\Fut7Fantasy.Api

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\infra\scripts\demo-reset.ps1
#>
[CmdletBinding()]
param(
    # Nome do banco recriado. O seed recusa qualquer nome sem "Demo".
    [string] $Banco = 'Fut7Fantasy_Demo'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositorio = (Resolve-Path (Join-Path (Join-Path $PSScriptRoot '..') '..')).Path
$arquivoEnv = Join-Path $repositorio '.env'
$projetoDemo = Join-Path $repositorio 'src\backend\src\Fut7Fantasy.Demo'

if (-not (Test-Path $arquivoEnv)) {
    throw 'Arquivo .env nao encontrado. Rode infra\scripts\bootstrap.ps1 antes.'
}

function Get-ValorEnv {
    param([string] $Chave)
    $linha = Get-Content $arquivoEnv | Where-Object { $_ -like "$Chave=*" } | Select-Object -First 1
    if ($linha) { return ($linha -replace "^$Chave=", '') }
    return $null
}

function New-Senha {
    $alfabeto = [char[]](([char]'a'..[char]'z') + ([char]'A'..[char]'Z') + ([char]'0'..[char]'9'))
    return -join (1..24 | ForEach-Object { $alfabeto | Get-Random })
}

# Senhas das contas demo: geradas uma vez e guardadas so no .env.
$semBom = New-Object System.Text.UTF8Encoding $false
foreach ($chave in 'DEMO_VIEWER_PASSWORD', 'DEMO_ORGANIZER_PASSWORD', 'DEMO_ADMIN_PASSWORD') {
    if (-not (Get-ValorEnv $chave)) {
        $conteudo = [System.IO.File]::ReadAllText($arquivoEnv)
        if ($conteudo.Length -gt 0 -and -not $conteudo.EndsWith("`n")) { $conteudo += "`n" }
        [System.IO.File]::WriteAllText($arquivoEnv, "$conteudo$chave=$(New-Senha)`n", $semBom)
        Write-Host "==> $chave gerada e guardada no .env" -ForegroundColor Cyan
    }
}

$senhaSql = Get-ValorEnv 'MSSQL_SA_PASSWORD'
$env:Database__ConnectionString = "Server=127.0.0.1,1433;Database=$Banco;User Id=sa;Password=$senhaSql;TrustServerCertificate=True"
$env:Demo__ViewerPassword = Get-ValorEnv 'DEMO_VIEWER_PASSWORD'
$env:Demo__OrganizerPassword = Get-ValorEnv 'DEMO_ORGANIZER_PASSWORD'
$env:Demo__AdminPassword = Get-ValorEnv 'DEMO_ADMIN_PASSWORD'
try {
    Write-Host "==> Recriando $Banco e contando a historia da Copa" -ForegroundColor Cyan
    dotnet run --project $projetoDemo -- reset --banco $Banco
    $codigo = $LASTEXITCODE
}
finally {
    foreach ($variavel in 'Database__ConnectionString', 'Demo__ViewerPassword', 'Demo__OrganizerPassword', 'Demo__AdminPassword') {
        Remove-Item "Env:\$variavel" -ErrorAction SilentlyContinue
    }
}

exit $codigo
