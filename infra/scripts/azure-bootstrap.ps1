#Requires -Version 5.1
<#
.SYNOPSIS
    Prepara a assinatura Azure e o repositorio para o deploy da demonstracao (Fase 17).
    Roda uma vez, na maquina de quem e dono da assinatura.

.DESCRIPTION
    Com o az e o gh logados:

      - registra os provedores de recurso que o deploy usa;
      - cria o grupo de recursos;
      - cria a identidade do GitHub Actions: um app no Entra com credencial federada (OIDC),
        sem senha nenhuma, valida so para o ambiente azure-demo deste repositorio;
      - da a ela Contributor so no grupo de recursos;
      - cria o ambiente azure-demo no GitHub, as variaveis do deploy e os segredos das
        senhas da demo na nuvem, que tambem ficam no .env local (fora do Git);
      - liga o orcamento do grupo, com alerta por e-mail a qualquer gasto real.

    Nada disso custa. Os recursos que rodam a aplicacao (infra/bicep/main.bicep) sao
    criados pelo workflow de deploy. Rodar de novo nao duplica nada.

    Para desligar tudo depois:
      az group delete --name rg-cartola-varzea
      az ad app delete --id <AZURE_CLIENT_ID>

.EXAMPLE
    az login
    gh auth login
    powershell -ExecutionPolicy Bypass -File .\infra\scripts\azure-bootstrap.ps1 -Email voce@exemplo.com
#>
[CmdletBinding()]
param(
    # Quem recebe os alertas do orcamento.
    [Parameter(Mandatory = $true)] [string] $Email,
    [string] $Location = 'brazilsouth',
    [string] $ResourceGroup = 'rg-cartola-varzea',
    # Vira o endereco https://<nome>.azurewebsites.net; precisa estar livre na Azure inteira.
    [string] $AppName = 'cartola-varzea',
    [string] $Repository = 'pedromeireles23/cartola-varzea',
    [string] $Environment = 'azure-demo'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositorio = (Resolve-Path (Join-Path (Join-Path $PSScriptRoot '..') '..')).Path
$arquivoEnv = Join-Path $repositorio '.env'
$nomeIdentidade = 'github-cartola-varzea'

function Invoke-Comando {
    param([string] $Programa, [string[]] $Argumentos)
    $saida = & $Programa @Argumentos
    if ($LASTEXITCODE -ne 0) { throw "$Programa $($Argumentos -join ' ') falhou." }
    return $saida
}

function New-Senha {
    $alfabeto = [char[]](([char]'a'..[char]'z') + ([char]'A'..[char]'Z') + ([char]'0'..[char]'9'))
    $bytes = New-Object byte[] 24
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return -join ($bytes | ForEach-Object { $alfabeto[$_ % $alfabeto.Length] })
}

function Get-ValorEnv {
    param([string] $Chave)
    if (-not (Test-Path $arquivoEnv)) { return $null }
    $linha = Get-Content $arquivoEnv | Where-Object { $_ -like "$Chave=*" } | Select-Object -First 1
    if ($linha) { return ($linha -replace "^$Chave=", '') }
    return $null
}

function Set-ValorEnv {
    param([string] $Chave, [string] $Valor)
    $semBom = New-Object System.Text.UTF8Encoding $false
    $conteudo = if (Test-Path $arquivoEnv) { [System.IO.File]::ReadAllText($arquivoEnv) } else { '' }
    if ($conteudo.Length -gt 0 -and -not $conteudo.EndsWith("`n")) { $conteudo += "`n" }
    [System.IO.File]::WriteAllText($arquivoEnv, "$conteudo$Chave=$Valor`n", $semBom)
}

$conta = Invoke-Comando az @('account', 'show', '--output', 'json') | ConvertFrom-Json
$assinatura = $conta.id
$tenant = $conta.tenantId
Write-Host "==> Assinatura: $($conta.name) ($assinatura)" -ForegroundColor Cyan

Write-Host '==> Registrando provedores de recurso' -ForegroundColor Cyan
foreach ($provedor in 'Microsoft.Web', 'Microsoft.Sql', 'Microsoft.OperationalInsights', 'Microsoft.Insights',
    'Microsoft.ManagedIdentity', 'Microsoft.Consumption') {
    Invoke-Comando az @('provider', 'register', '--namespace', $provedor, '--output', 'none') | Out-Null
}

# Corpos JSON vao por arquivo: o az do Windows e um .cmd, e aspas na linha de comando se perdem.
$consulta = New-TemporaryFile
@{ name = $AppName; type = 'Microsoft.Web/sites' } | ConvertTo-Json | Set-Content -Path $consulta -Encoding Ascii
$livre = Invoke-Comando az @('rest', '--method', 'post',
    '--url', "https://management.azure.com/subscriptions/$assinatura/providers/Microsoft.Web/checknameavailability?api-version=2022-03-01",
    '--body', "@$consulta", '--query', 'nameAvailable', '--output', 'tsv')
Remove-Item $consulta
$jaEhNosso = Invoke-Comando az @('webapp', 'list', '--query', "length([?name=='$AppName'])", '--output', 'tsv')
if ($livre -ne 'true' -and $jaEhNosso -eq '0') {
    throw "O nome $AppName ja esta em uso na Azure. Rode de novo com -AppName <outro nome>."
}

Write-Host "==> Grupo de recursos $ResourceGroup em $Location" -ForegroundColor Cyan
Invoke-Comando az @('group', 'create', '--name', $ResourceGroup, '--location', $Location, '--output', 'none') | Out-Null

Write-Host "==> Identidade do GitHub Actions ($nomeIdentidade)" -ForegroundColor Cyan
$appId = Invoke-Comando az @('ad', 'app', 'list', '--display-name', $nomeIdentidade, '--query', '[0].appId', '--output', 'tsv')
if (-not $appId) {
    $appId = Invoke-Comando az @('ad', 'app', 'create', '--display-name', $nomeIdentidade, '--query', 'appId', '--output', 'tsv')
}
$spId = Invoke-Comando az @('ad', 'sp', 'list', '--filter', "appId eq '$appId'", '--query', '[0].id', '--output', 'tsv')
if (-not $spId) {
    $spId = Invoke-Comando az @('ad', 'sp', 'create', '--id', $appId, '--query', 'id', '--output', 'tsv')
}

# OIDC: o GitHub troca o token do job por um da Azure. Vale so para jobs no ambiente azure-demo.
$sujeito = "repo:${Repository}:environment:$Environment"
$existentes = Invoke-Comando az @('ad', 'app', 'federated-credential', 'list', '--id', $appId,
    '--query', "length([?subject=='$sujeito'])", '--output', 'tsv')
if ($existentes -eq '0') {
    $credencial = New-TemporaryFile
    @{
        name      = 'github-azure-demo'
        issuer    = 'https://token.actions.githubusercontent.com'
        subject   = $sujeito
        audiences = @('api://AzureADTokenExchange')
    } | ConvertTo-Json | Set-Content -Path $credencial -Encoding Ascii
    Invoke-Comando az @('ad', 'app', 'federated-credential', 'create', '--id', $appId,
        '--parameters', "@$credencial", '--output', 'none') | Out-Null
    Remove-Item $credencial
}

$escopo = "/subscriptions/$assinatura/resourceGroups/$ResourceGroup"
$papeis = Invoke-Comando az @('role', 'assignment', 'list', '--assignee', $spId, '--scope', $escopo,
    '--role', 'Contributor', '--query', 'length(@)', '--output', 'tsv')
if ($papeis -eq '0') {
    # A identidade recem-criada leva alguns segundos para aparecer para o controle de acesso.
    for ($tentativa = 1; ; $tentativa++) {
        & az role assignment create --assignee-object-id $spId --assignee-principal-type ServicePrincipal `
            --role Contributor --scope $escopo --output none
        if ($LASTEXITCODE -eq 0) { break }
        if ($tentativa -ge 6) { throw 'Nao foi possivel dar Contributor a identidade do GitHub Actions.' }
        Start-Sleep -Seconds 10
    }
}

Write-Host "==> Ambiente $Environment, variaveis e segredos no GitHub" -ForegroundColor Cyan
Invoke-Comando gh @('api', '--method', 'PUT', "repos/$Repository/environments/$Environment", '--silent') | Out-Null
$variaveis = [ordered]@{
    AZURE_CLIENT_ID           = $appId
    AZURE_TENANT_ID           = $tenant
    AZURE_SUBSCRIPTION_ID     = $assinatura
    AZURE_RESOURCE_GROUP      = $ResourceGroup
    AZURE_APP_NAME            = $AppName
    AZURE_SQL_ADMIN_NAME      = $nomeIdentidade
    AZURE_SQL_ADMIN_OBJECT_ID = $spId
}
foreach ($variavel in $variaveis.GetEnumerator()) {
    Invoke-Comando gh @('variable', 'set', $variavel.Key, '--repo', $Repository, '--body', $variavel.Value) | Out-Null
}

# Senhas das contas da demo na nuvem: geradas uma vez, guardadas no .env para o Pedro
# entrar como organizacao ou administracao, e como segredo do ambiente para o reset.
$segredos = [ordered]@{
    DEMO_VIEWER_PASSWORD    = 'AZURE_DEMO_VIEWER_PASSWORD'
    DEMO_ORGANIZER_PASSWORD = 'AZURE_DEMO_ORGANIZER_PASSWORD'
    DEMO_ADMIN_PASSWORD     = 'AZURE_DEMO_ADMIN_PASSWORD'
}
foreach ($segredo in $segredos.GetEnumerator()) {
    $valor = Get-ValorEnv $segredo.Value
    if (-not $valor) {
        $valor = New-Senha
        Set-ValorEnv $segredo.Value $valor
    }
    $valor | gh secret set $segredo.Key --repo $Repository --env $Environment
    if ($LASTEXITCODE -ne 0) { throw "gh secret set $($segredo.Key) falhou." }
}

Write-Host '==> Orcamento com alerta por e-mail' -ForegroundColor Cyan
Invoke-Comando az @('deployment', 'group', 'create', '--resource-group', $ResourceGroup,
    '--template-file', (Join-Path $repositorio 'infra\bicep\orcamento.bicep'),
    '--parameters', "contactEmail=$Email", '--output', 'none') | Out-Null

Write-Host ''
Write-Host 'Pronto. Proximos passos:' -ForegroundColor Green
Write-Host '  gh workflow run deploy.yml       # provisiona e publica'
Write-Host '  gh workflow run demo-reset.yml   # conta a historia da demo no banco novo'
Write-Host "  https://$AppName.azurewebsites.net"
