// infra/main.bicepparam — Parameter file for production deployment
// Copy and fill in values — do NOT commit real secrets to source control.

using './main.bicep'

param location = 'uksouth'
param environment = 'prod'
param appName = 'catalogue'
param sqlAdminLogin = 'catalogueadmin'
param sqlAdminPassword = readEnvironmentVariable('SQL_ADMIN_PASSWORD')  // set as CI/CD secret
