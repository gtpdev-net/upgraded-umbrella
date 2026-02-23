// infra/entra-app-roles.bicep
// Entra App Registration with Catalogue roles.
// NOTE: Bicep/ARM cannot fully configure Entra App Registrations.
//       This template outputs the necessary manifest snippet; the app registration
//       itself must be created via Azure CLI or the portal and the manifest patched.
//
// Run once per environment:
//   az ad app create --display-name "Source Database Catalogue" \
//     --identifier-uris "api://catalogue-prod" \
//     --app-roles @infra/app-roles.json

// The app-roles.json manifest snippet (see below) defines the three roles.
// After creating the app registration:
//  1. Note the Application (client) ID and Tenant ID.
//  2. Update appsettings.Production.json AzureAd section (ClientId, TenantId, Domain).
//  3. Assign users/groups to roles via Entra ID → Enterprise Applications → Catalogue → Users and groups.

output appRolesManifest array = [
  {
    allowedMemberTypes: ['User']
    description: 'Read-only access to the Source Database Catalogue.'
    displayName: 'Catalogue Reader'
    id: 'aaaaaaaa-0000-1111-2222-bbbbbbbbbbbb'   // replace with a real GUID
    isEnabled: true
    value: 'Catalogue.Reader'
  }
  {
    allowedMemberTypes: ['User']
    description: 'Can create and edit catalogue entries (servers, databases, tables, columns).'
    displayName: 'Catalogue Editor'
    id: 'bbbbbbbb-0000-1111-2222-cccccccccccc'   // replace with a real GUID
    isEnabled: true
    value: 'Catalogue.Editor'
  }
  {
    allowedMemberTypes: ['User']
    description: 'Full administrative access including import and bulk operations.'
    displayName: 'Catalogue Admin'
    id: 'cccccccc-0000-1111-2222-dddddddddddd'   // replace with a real GUID
    isEnabled: true
    value: 'Catalogue.Admin'
  }
]
