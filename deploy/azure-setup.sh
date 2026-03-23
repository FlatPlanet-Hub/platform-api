#!/usr/bin/env bash
# ============================================================
# FlatPlanet Platform API — One-time Azure resource setup
# Run this ONCE from your local machine with Azure CLI logged in.
# Usage: bash deploy/azure-setup.sh
# ============================================================

set -euo pipefail

SUBSCRIPTION="cbb3d43b-12a9-4cd7-8c1a-ee68e65861d2"
RESOURCE_GROUP="FPPlatform"
LOCATION="southeastasia"
APP_NAME="flatplanet-api"
PLAN_NAME="flatplanet-api-plan"
KEYVAULT_NAME="flatplanet-kv"
SP_NAME="flatplanet-api-deploy"

echo "==> Setting subscription..."
az account set --subscription "$SUBSCRIPTION"

# ── App Service Plan (Linux B2 — required for .NET 10) ──────
echo "==> Creating App Service Plan..."
az appservice plan create \
  --name "$PLAN_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --sku B2 \
  --is-linux

# ── App Service ─────────────────────────────────────────────
echo "==> Creating App Service..."
az webapp create \
  --name "$APP_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --plan "$PLAN_NAME" \
  --runtime "DOTNETCORE:10.0"

# Enable HTTPS only
az webapp update \
  --name "$APP_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --https-only true

# ── System-assigned Managed Identity ────────────────────────
echo "==> Enabling Managed Identity..."
PRINCIPAL_ID=$(az webapp identity assign \
  --name "$APP_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --query principalId --output tsv)

echo "   Principal ID: $PRINCIPAL_ID"

# ── Key Vault ────────────────────────────────────────────────
echo "==> Creating Key Vault..."
az keyvault create \
  --name "$KEYVAULT_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --sku standard \
  --enable-rbac-authorization false

# Grant the App Service identity read access to secrets
az keyvault set-policy \
  --name "$KEYVAULT_NAME" \
  --object-id "$PRINCIPAL_ID" \
  --secret-permissions get list

echo ""
echo "==> Key Vault ready. Now add your secrets:"
echo ""
echo "Run the following commands, replacing <VALUE> with real values:"
echo ""
echo "  az keyvault secret set --vault-name $KEYVAULT_NAME --name Jwt--SecretKey        --value '<32+ char random string>'"
echo "  az keyvault secret set --vault-name $KEYVAULT_NAME --name Encryption--Key       --value '<32+ char random string>'"
echo "  az keyvault secret set --vault-name $KEYVAULT_NAME --name GitHub--ClientId      --value '<your GitHub OAuth App client ID>'"
echo "  az keyvault secret set --vault-name $KEYVAULT_NAME --name GitHub--ClientSecret  --value '<your GitHub OAuth App client secret>'"
echo "  az keyvault secret set --vault-name $KEYVAULT_NAME --name Supabase--AdminUser   --value 'postgres.<your-project-ref>'"
echo "  az keyvault secret set --vault-name $KEYVAULT_NAME --name Supabase--AdminPassword --value '<your Supabase DB password>'"
echo ""

# ── App Service configuration (Key Vault references) ────────
echo "==> Configuring App Service app settings (Key Vault references)..."
KV_URI="https://${KEYVAULT_NAME}.vault.azure.net/secrets"

az webapp config appsettings set \
  --name "$APP_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --settings \
    "ASPNETCORE_ENVIRONMENT=Production" \
    "Jwt__SecretKey=@Microsoft.KeyVault(SecretUri=${KV_URI}/Jwt--SecretKey/)" \
    "Encryption__Key=@Microsoft.KeyVault(SecretUri=${KV_URI}/Encryption--Key/)" \
    "GitHub__ClientId=@Microsoft.KeyVault(SecretUri=${KV_URI}/GitHub--ClientId/)" \
    "GitHub__ClientSecret=@Microsoft.KeyVault(SecretUri=${KV_URI}/GitHub--ClientSecret/)" \
    "GitHub__RedirectUri=https://${APP_NAME}.azurewebsites.net/api/auth/oauth/github/callback" \
    "GitHub__FrontendCallbackUrl=https://${APP_NAME}.azurewebsites.net/auth/callback" \
    "Supabase__AdminUser=@Microsoft.KeyVault(SecretUri=${KV_URI}/Supabase--AdminUser/)" \
    "Supabase__AdminPassword=@Microsoft.KeyVault(SecretUri=${KV_URI}/Supabase--AdminPassword/)" \
    "Supabase__Host=aws-0-us-east-1.pooler.supabase.com" \
    "Supabase__Port=6543" \
    "Supabase__Database=postgres" \
    "Cors__AllowedOrigins__0=https://${APP_NAME}.azurewebsites.net"

# ── GitHub Actions service principal ────────────────────────
echo ""
echo "==> Creating GitHub Actions service principal..."
echo "    Copy the JSON below and add it as a GitHub secret named AZURE_CREDENTIALS"
echo ""
az ad sp create-for-rbac \
  --name "$SP_NAME" \
  --role contributor \
  --scopes "/subscriptions/${SUBSCRIPTION}/resourceGroups/${RESOURCE_GROUP}" \
  --sdk-auth

echo ""
echo "==> Setup complete!"
echo "    App URL: https://${APP_NAME}.azurewebsites.net"
echo "    Key Vault: https://${KEYVAULT_NAME}.vault.azure.net"
echo ""
echo "Next steps:"
echo "  1. Add the JSON above as GitHub secret: AZURE_CREDENTIALS"
echo "  2. Run the 'az keyvault secret set' commands shown above"
echo "  3. Update your GitHub OAuth App callback URL to:"
echo "     https://${APP_NAME}.azurewebsites.net/api/auth/oauth/github/callback"
echo "  4. Push to main — GitHub Actions will deploy automatically"
