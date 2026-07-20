#!/usr/bin/env sh
set -eu

apply_migration() {
  name="$1"
  project="$2"
  startup="$3"
  connection="$4"

  echo "Applying ${name} migrations..."
  dotnet ef database update \
    --project "$project" \
    --startup-project "$startup" \
    --configuration Release \
    --no-build \
    --connection "$connection"
}

apply_migration \
  "Auth" \
  "src/Services/Auth/Infrastructure/TaxVision.Auth.Infrastructure.csproj" \
  "src/Services/Auth/Api/TaxVision.Auth.Api.csproj" \
  "$AUTH_DB_CONNECTION"

apply_migration \
  "Tenant" \
  "src/Services/Tenant/TaxVision.Tenant.Infrastructure/TaxVision.Tenant.Infrastructure.csproj" \
  "src/Services/Tenant/TaxVision.Tenant.Api/TaxVision.Tenant.Api.csproj" \
  "$TENANT_DB_CONNECTION"

apply_migration \
  "Customer" \
  "src/Services/Customer/TaxVision.Customer.Infrastructure/TaxVision.Customer.Infrastructure.csproj" \
  "src/Services/Customer/TaxVision.Customer.Api/TaxVision.Customer.Api.csproj" \
  "$CUSTOMER_DB_CONNECTION"

apply_migration \
  "Subscription" \
  "src/Services/Subscription/TaxVision.Subscription.Infrastructure/TaxVision.Subscription.Infrastructure.csproj" \
  "src/Services/Subscription/TaxVision.Subscription.Api/TaxVision.Subscription.Api.csproj" \
  "$SUBSCRIPTION_DB_CONNECTION"

apply_migration \
  "Notification" \
  "src/Services/Notification/TaxVision.Notification.Infrastructure/TaxVision.Notification.Infrastructure.csproj" \
  "src/Services/Notification/TaxVision.Notification.Api/TaxVision.Notification.Api.csproj" \
  "$NOTIFICATION_DB_CONNECTION"

apply_migration \
  "CloudStorage" \
  "src/Services/CloudStorage/TaxVision.CloudStorage.Infrastructure/TaxVision.CloudStorage.Infrastructure.csproj" \
  "src/Services/CloudStorage/TaxVision.CloudStorage.Api/TaxVision.CloudStorage.Api.csproj" \
  "$CLOUDSTORAGE_DB_CONNECTION"

apply_migration \
  "Signature" \
  "src/Services/Signature/TaxVision.Signature.Infrastructure/TaxVision.Signature.Infrastructure.csproj" \
  "src/Services/Signature/TaxVision.Signature.Api/TaxVision.Signature.Api.csproj" \
  "$SIGNATURE_DB_CONNECTION"

apply_migration \
  "Postmaster" \
  "src/Services/Postmaster/TaxVision.Postmaster.Infrastructure/TaxVision.Postmaster.Infrastructure.csproj" \
  "src/Services/Postmaster/TaxVision.Postmaster.Api/TaxVision.Postmaster.Api.csproj" \
  "$POSTMASTER_DB_CONNECTION"

apply_migration \
  "Scribe" \
  "src/Services/Scribe/TaxVision.Scribe.Infrastructure/TaxVision.Scribe.Infrastructure.csproj" \
  "src/Services/Scribe/TaxVision.Scribe.Api/TaxVision.Scribe.Api.csproj" \
  "$SCRIBE_DB_CONNECTION"

apply_migration \
  "Connectors" \
  "src/Services/Connectors/TaxVision.Connectors.Infrastructure/TaxVision.Connectors.Infrastructure.csproj" \
  "src/Services/Connectors/TaxVision.Connectors.Api/TaxVision.Connectors.Api.csproj" \
  "$CONNECTORS_DB_CONNECTION"

apply_migration \
  "Correspondence" \
  "src/Services/Correspondence/TaxVision.Correspondence.Infrastructure/TaxVision.Correspondence.Infrastructure.csproj" \
  "src/Services/Correspondence/TaxVision.Correspondence.Api/TaxVision.Correspondence.Api.csproj" \
  "$CORRESPONDENCE_DB_CONNECTION"

apply_migration \
  "PaymentApp" \
  "src/Services/PaymentApp/TaxVision.PaymentApp.Infrastructure/TaxVision.PaymentApp.Infrastructure.csproj" \
  "src/Services/PaymentApp/TaxVision.PaymentApp.Api/TaxVision.PaymentApp.Api.csproj" \
  "$PAYMENTAPP_DB_CONNECTION"

apply_migration \
  "PaymentClient" \
  "src/Services/PaymentClient/TaxVision.PaymentClient.Infrastructure/TaxVision.PaymentClient.Infrastructure.csproj" \
  "src/Services/PaymentClient/TaxVision.PaymentClient.Api/TaxVision.PaymentClient.Api.csproj" \
  "$PAYMENTCLIENT_DB_CONNECTION"

apply_migration \
  "Growth" \
  "src/Services/Growth/TaxVision.Growth.Infrastructure/TaxVision.Growth.Infrastructure.csproj" \
  "src/Services/Growth/TaxVision.Growth.Api/TaxVision.Growth.Api.csproj" \
  "$GROWTH_DB_CONNECTION"

echo "All TaxVision migrations were applied successfully."
