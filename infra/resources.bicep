// infra/resources.bicep — All resources deployed into the resource group

@description('Azure region')
param location string

@description('Environment name')
param environment string

@description('Base app name')
param appName string

@description('SQL admin login')
param sqlAdminLogin string

@secure()
@description('SQL admin password')
param sqlAdminPassword string

// ── Naming convention ────────────────────────────────────────────────────────
var suffix = '${appName}-${environment}'
var planName = 'plan-${suffix}'
var webAppName = 'app-${suffix}'
var sqlServerName = 'sql-${suffix}'
var dbName = 'CatalogueDb'
var kvName = 'kv-${suffix}'     // max 24 chars — keep appName short

// ── App Service Plan (B2 Linux) ──────────────────────────────────────────────
resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  sku: {
    name: 'B2'
    tier: 'Basic'
  }
  kind: 'linux'
  properties: {
    reserved: true   // required for Linux
  }
}

// ── App Service ──────────────────────────────────────────────────────────────
resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: environment == 'prod' ? 'Production' : 'Development'
        }
        {
          name: 'KeyVaultName'
          value: kvName
        }
      ]
    }
  }
}

// ── Azure SQL Server ─────────────────────────────────────────────────────────
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'   // restrict to Azure services only via firewall rule below
  }
}

resource sqlFirewallAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// ── Azure SQL Database (General Purpose S2) ───────────────────────────────────
resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: dbName
  location: location
  sku: {
    name: 'S2'
    tier: 'Standard'
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 10737418240   // 10 GB
    zoneRedundant: false
  }
}

// ── Key Vault ────────────────────────────────────────────────────────────────
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: kvName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true     // use RBAC rather than access policies
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    publicNetworkAccess: 'Enabled'
  }
}

// ── Key Vault connection string secret ──────────────────────────────────────
var connectionString = 'Server=${sqlServer.properties.fullyQualifiedDomainName};Database=${dbName};User Id=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=true;TrustServerCertificate=false;'

resource kvSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'CatalogueDbConnectionString'
  properties: {
    value: connectionString
  }
}

// ── RBAC: App Service managed identity → Key Vault Secrets User ──────────────
var kvSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'  // Key Vault Secrets User built-in role

resource kvRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, webApp.id, kvSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ── Outputs ──────────────────────────────────────────────────────────────────
output webAppName string = webApp.name
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output keyVaultName string = keyVault.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output webAppPrincipalId string = webApp.identity.principalId
