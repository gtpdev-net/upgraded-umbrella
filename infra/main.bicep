// infra/main.bicep — Source Database Catalogue infrastructure
// Deploy: az deployment sub create --location uksouth --template-file infra/main.bicep --parameters infra/main.bicepparam

targetScope = 'subscription'

@description('Primary Azure region for all resources')
param location string = 'uksouth'

@description('Environment name (dev | staging | prod)')
@allowed(['dev', 'staging', 'prod'])
param environment string = 'prod'

@description('Base name used to derive all resource names')
param appName string = 'catalogue'

@description('SQL Server administrator login')
param sqlAdminLogin string = 'catalogueadmin'

@secure()
@description('SQL Server administrator password')
param sqlAdminPassword string

// ── Resource group ──────────────────────────────────────────────────────────
resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${appName}-${environment}'
  location: location
}

// ── Deploy resources into the resource group ─────────────────────────────────
module resources 'resources.bicep' = {
  name: 'catalogue-resources'
  scope: rg
  params: {
    location: location
    environment: environment
    appName: appName
    sqlAdminLogin: sqlAdminLogin
    sqlAdminPassword: sqlAdminPassword
  }
}

output webAppName string = resources.outputs.webAppName
output webAppUrl string = resources.outputs.webAppUrl
output keyVaultName string = resources.outputs.keyVaultName
output sqlServerFqdn string = resources.outputs.sqlServerFqdn
