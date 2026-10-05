// Demonstração do Cartola Várzea na Azure, com teto de custo zero (Fase 17): só recursos
// com camada sempre gratuita. Implantado pelo workflow de deploy a cada versão; idempotente.
//
//   az deployment group create -g <grupo> -f infra/bicep/main.bicep \
//     -p sqlAdminObjectId=<object id> sqlAdminLogin=<nome>

targetScope = 'resourceGroup'

@description('Nome da Web App, que vira o endereço https://<nome>.azurewebsites.net. Único na Azure inteira.')
param appName string = 'cartola-varzea'

param location string = resourceGroup().location

@description('Object ID de quem administra o SQL: a identidade do GitHub Actions, que aplica migrations e o reset.')
param sqlAdminObjectId string

@description('Nome exibido dessa identidade.')
param sqlAdminLogin string

@allowed([
  'Application'
  'User'
  'Group'
])
param sqlAdminPrincipalType string = 'Application'

@description('O reset da demo só aceita banco com "Demo" no nome.')
param databaseName string = 'Fut7Fantasy_Demo'

@description('Minutos sem conexão até o banco pausar. Menos é melhor para a cota gratuita: cada vez que acorda, ele fica ligado ao menos isso.')
param sqlAutoPauseDelayMinutes int = 15

@description('Teto diário de ingestão do Log Analytics, em GB; o mês fica dentro da franquia gratuita.')
param logDailyQuotaGb string = '0.1'

@description('Conta que o botão "Entrar como visitante" abre; precisa existir com o papel DemoViewer.')
param demoViewerEmail string = 'visitante@demo.cartola-varzea.test'

var suffix = take(uniqueString(resourceGroup().id), 6)

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${appName}'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: json(logDailyQuotaGb)
    }
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${appName}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
    IngestionMode: 'LogAnalytics'
  }
}

// Identidade própria, e não a do sistema: o pipeline cria o usuário dela no SQL pelo
// clientId (WITH SID ... TYPE = E), sem precisar ler o diretório do Entra.
resource appIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${appName}'
  location: location
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = {
  name: 'sql-${appName}-${suffix}'
  location: location
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      tenantId: subscription().tenantId
      principalType: sqlAdminPrincipalType
    }
  }
}

// O F1 não tem integração com rede virtual: a Web App chega ao SQL pela rede pública da
// Azure. Só autenticação Entra é aceita, então a regra abre a porta, não a senha.
resource sqlAllowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Oferta gratuita: 100 mil vCore-segundos e 32 GB por mês; esgotou, pausa até o mês
// seguinte em vez de cobrar.
resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: databaseName
  location: location
  sku: {
    name: 'GP_S_Gen5_2'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    autoPauseDelay: sqlAutoPauseDelayMinutes
    requestedBackupStorageRedundancy: 'Local'
    collation: 'SQL_Latin1_General_CP1_CI_AS'
  }
}

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'asp-${appName}'
  location: location
  kind: 'linux'
  sku: {
    name: 'F1'
    tier: 'Free'
  }
  properties: {
    reserved: true
  }
}

resource app 'Microsoft.Web/sites@2024-04-01' = {
  name: appName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${appIdentity.id}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: false
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          // O TLS termina no balanceador: sem isto, o antiforgery derruba com 500 e todo
          // cliente divide o IP do balanceador no rate limit (ReverseProxyTests).
          name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'
          value: 'true'
        }
        {
          // Connect Timeout alto: o banco pausado leva até um minuto para acordar.
          name: 'Database__ConnectionString'
          value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${databaseName};Authentication=Active Directory Managed Identity;User Id=${appIdentity.properties.clientId};Encrypt=True;Connect Timeout=60'
        }
        {
          name: 'Authentication__PublicOrigin'
          value: 'https://${appName}.azurewebsites.net'
        }
        {
          name: 'DemoAccess__Enabled'
          value: 'true'
        }
        {
          name: 'DemoAccess__ViewerEmail'
          value: demoViewerEmail
        }
        {
          // Sem provedor de e-mail: cadastro e recuperação de senha fechados (04 §11).
          name: 'Email__Enabled'
          value: 'false'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: insights.properties.ConnectionString
        }
        {
          name: 'SCM_DO_BUILD_DURING_DEPLOYMENT'
          value: 'false'
        }
      ]
    }
  }
}

// Publicação só pela identidade do GitHub Actions; nada de usuário e senha de FTP ou SCM.
resource ftpCredentials 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: app
  name: 'ftp'
  properties: {
    allow: false
  }
}

resource scmCredentials 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: app
  name: 'scm'
  properties: {
    allow: false
  }
}

output appName string = app.name
output appUrl string = 'https://${app.properties.defaultHostName}'
output sqlServerName string = sqlServer.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = database.name
output appIdentityName string = appIdentity.name
output appIdentityClientId string = appIdentity.properties.clientId
