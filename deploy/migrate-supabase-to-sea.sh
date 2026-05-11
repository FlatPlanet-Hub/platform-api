#!/usr/bin/env bash
# ============================================================
# Migrate flatplanet-api Supabase pooler: Tokyo → Southeast Asia
#
# Background: pjbxerrvmlhnfeivxfjd is hosted in ap-northeast-1
# (Tokyo). The flatplanet-api App Service is in southeastasia
# (Singapore), causing ~100ms cross-region latency on every
# DB query. Moving to ap-southeast-1 co-locates the pooler
# with the App Service.
#
# Pre-requisites (done the night before):
#   1. Supabase project already migrated to ap-southeast-1 via
#      the Supabase dashboard (same steps used for security-api).
#   2. Verify new pooler is reachable:
#        psql "host=aws-1-ap-southeast-1.pooler.supabase.com port=6543 \
#              dbname=postgres user=postgres.pjbxerrvmlhnfeivxfjd \
#              password=c0d1ngw1thc1a@ude sslmode=require"
#
# Run: bash deploy/migrate-supabase-to-sea.sh
# ============================================================

set -euo pipefail

RESOURCE_GROUP="FPPlatform"
APP_NAME="flatplanet-api"
OLD_HOST="aws-1-ap-northeast-1.pooler.supabase.com"
NEW_HOST="aws-1-ap-southeast-1.pooler.supabase.com"

echo "==> [1/4] Verifying current Supabase host..."
CURRENT=$(az webapp config appsettings list \
  --resource-group "$RESOURCE_GROUP" \
  --name "$APP_NAME" \
  --query "[?name=='Supabase__Host'].value" -o tsv)

echo "    Current: $CURRENT"

if [ "$CURRENT" = "$NEW_HOST" ]; then
  echo "    Already pointing to SEA — nothing to do."
  exit 0
fi

if [ "$CURRENT" != "$OLD_HOST" ]; then
  echo "    ERROR: Unexpected host '$CURRENT'. Aborting."
  exit 1
fi

echo "==> [2/4] Updating Supabase__Host to $NEW_HOST..."
az webapp config appsettings set \
  --resource-group "$RESOURCE_GROUP" \
  --name "$APP_NAME" \
  --settings "Supabase__Host=$NEW_HOST" \
  --output none

echo "==> [3/4] Restarting $APP_NAME..."
az webapp restart \
  --resource-group "$RESOURCE_GROUP" \
  --name "$APP_NAME"

echo "    Waiting 30s for app to come back up..."
sleep 30

echo "==> [4/4] Health check..."
HEALTH_URL="https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net/health"
for i in 1 2 3; do
  STATUS=$(curl -s -o /dev/null -w "%{http_code}" --max-time 15 "$HEALTH_URL")
  echo "    Attempt $i: HTTP $STATUS"
  if [ "$STATUS" = "200" ]; then
    echo ""
    echo "✓ Migration complete. flatplanet-api is healthy on SEA pooler."
    exit 0
  fi
  sleep 10
done

echo ""
echo "✗ Health check failed after 3 attempts."
echo "  To rollback: az webapp config appsettings set \\"
echo "    --resource-group $RESOURCE_GROUP --name $APP_NAME \\"
echo "    --settings Supabase__Host=$OLD_HOST"
exit 1
