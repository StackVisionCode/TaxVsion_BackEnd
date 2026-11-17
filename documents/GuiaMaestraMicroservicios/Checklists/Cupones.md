# Checklist — Cupones y descuentos

Guía: [cap. 17, Descuentos, códigos y referidos](../GUIA_MAESTRA_CREACION_MICROSERVICIOS_TAXVISION.md#cap-17). Aplica cuando un servicio vende algo que puede llevar un código o un descuento de la suscripción.

## Antes de cobrar
- [ ] El precio y el descuento los calcula el servidor; el front solo muestra la vista previa.
- [ ] La vista previa usa el **mismo** cálculo que el cobro (como `AddOnCheckoutPricing` o `DiscountedCharge`).
- [ ] El descuento vigente se aplica con `DiscountedCharge.For/ForAsync` y el tipo de cargo correcto (`Base`, `Seats`, `AddOns`).
- [ ] El cobro nunca baja del mínimo del proveedor (`DiscountedCharge.MinimumChargeCents`).

## Canje de un código (orquestador)
- [ ] Cliente M2M propio hacia `internal/codes` con audiencia `taxvision-growth` y los scopes `growth.codes.quote/reserve/commit/cancel`; secretos en user-secrets (local) y en `production-extra` + `deploy.yml` (prod).
- [ ] Orden: cotizar → reservar → confirmar; si falla después de reservar, cancelar.
- [ ] Idempotency-Key estable por paso (`<prefijo>-<paso>:<id>`).
- [ ] Se mandan `Plan:<PlanId>` y `Offer:Subscription/<PlanId>/<ciclo>`; Tenant y Subject los pone Growth.
- [ ] Con varios códigos en la misma compra se manda `stackedWithCodeDefinitionIds`.
- [ ] Los errores `Codes.*` se traducen a un mensaje claro; uno de autorización o red es "no disponible", nunca "código inválido".
- [ ] Con `AutoApplyTransactions`, el estado local se toca **después** del commit en Growth.

## Registro y reversa
- [ ] PaymentApp guarda la promoción del cobro (bruto, descuento, fuente, reserva).
- [ ] Reembolso total devuelve el uso; parcial o contracargo no.

## Administración (PlatformAdmin)
- [ ] Alta, activación, suspensión/reactivación, nueva regla (`rules`), alcances (`PUT scopes`) y revocación por Postman.
- [ ] Antes de activar el apilado sobre códigos existentes, SELECT de los que tienen `AllowStacking = 0`.

## Pruebas
- [ ] Dominio: regla, alcance (Include/Exclude), apilado, suspensión, duración.
- [ ] Aplicación: cada error y el orden de llamadas del orquestador.
- [ ] `.Integration.` en base temporal para lo que agrega hijos a un agregado guardado.
- [ ] Front: línea de descuento y neto desde la vista previa; errores traducidos.
- [ ] T-VIVO: código válido en su plan e inválido en otro; canje y vista previa que baja.
