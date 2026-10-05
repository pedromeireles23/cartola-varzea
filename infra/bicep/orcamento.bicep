// Orçamento do grupo de recursos da demonstração (Fase 17). Implantado uma vez pelo
// infra/scripts/azure-bootstrap.ps1, e não a cada deploy: a data de início de um
// orçamento não muda depois de criado. Orçamento só avisa; não desliga nada.

targetScope = 'resourceGroup'

@description('Quem recebe os alertas.')
param contactEmail string

@description('Teto mensal, na moeda da conta de cobrança. O plano é gastar zero.')
param amount int = 1

param startDate string = utcNow('yyyy-MM-01')

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: 'orcamento-cartola-varzea'
  properties: {
    category: 'Cost'
    amount: amount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: startDate
    }
    notifications: {
      // 1% do teto: qualquer gasto real já avisa.
      qualquerGasto: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 1
        thresholdType: 'Actual'
        contactEmails: [
          contactEmail
        ]
      }
      previsaoAcimaDoTeto: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [
          contactEmail
        ]
      }
    }
  }
}
