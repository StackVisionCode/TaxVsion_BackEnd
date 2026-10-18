# Landing — Reconcile al return del hosted checkout

## Problema

Con PayPal, el usuario vuelve del checkout con `?checkout=success&token=...&PayerID=...` pero el
`SaaSPayment` queda `Processing` hasta que llega el webhook (segundos o minutos). Mientras tanto el
Account muestra el banner "There is already a purchase waiting for payment". Con Stripe el webhook es
casi instantáneo, por eso se nota solo con PayPal.

## Solución

El backend ya expone `POST /payments-app/saas-payments/{paymentId}/reconcile`. Llama a
`FinalizeHostedCheckoutAsync` del adapter (Capture Order en PayPal, retrieve+confirm en Stripe), marca
el pago Succeeded/Failed y publica el integration event del type correcto. Idempotente: si el webhook
ya cerró el pago, devuelve el estado terminal sin tocar al provider.

El landing tiene que:
1. Al abrir cada hosted checkout, guardar el `paymentId` que devuelve el backend en
   `sessionStorage` con una llave estable por flujo.
2. Al cargar las páginas donde el usuario retorna (`/account/add-ons`, `/account/plan`,
   `/account/seats`), si la URL trae `?checkout=success`, leer el `paymentId` del `sessionStorage`,
   llamar al endpoint reconcile, limpiar el storage, limpiar los query params y refrescar la lista.

## Archivos a tocar en el landing

| Archivo | Cambio |
|---|---|
| `src/app/core/account/account-subscription.service.ts` (o similar) | Nuevo método `reconcileHostedCheckout(paymentId): Observable<ReconcileResponse>` → `POST /payments-app/saas-payments/${paymentId}/reconcile` |
| `src/app/account/pages/add-ons/account-add-ons.component.ts` | Al cargar, si `?checkout=success` → reconcile + reload |
| `src/app/account/pages/plan/account-plan.component.ts` (o renewal page) | Mismo patrón |
| `src/app/account/pages/seats/account-seats.component.ts` | Mismo patrón |
| Cada caller de "iniciar checkout" (`createAddOnCheckout`, `createSeatCheckout`, `createRenewalCheckout`) | Al recibir el `paymentId` de la respuesta del backend, guardarlo en `sessionStorage` con la llave del flujo |

## Pseudocódigo del landing

```typescript
// Al iniciar el checkout (dentro del componente que hace click en "Buy Add-On", "Pay now", etc.)
this.subscriptionService.createAddOnCheckout(payload).subscribe(response => {
  // Guardar el paymentId para reconciliar al return. Llave por flujo para no mezclar.
  sessionStorage.setItem(`checkout:paymentId:addon`, response.paymentId);
  window.location.href = response.checkoutUrl;
});
```

```typescript
// En AccountAddOnsComponent (o equivalente), en ngOnInit o resolver:
async handleCheckoutReturn() {
  const checkoutSuccess = this.route.snapshot.queryParamMap.get('checkout') === 'success';
  if (!checkoutSuccess) return;

  const paymentId = sessionStorage.getItem('checkout:paymentId:addon');
  sessionStorage.removeItem('checkout:paymentId:addon');

  if (paymentId) {
    try {
      await firstValueFrom(this.subscriptionService.reconcileHostedCheckout(paymentId));
    } catch {
      // Reconcile es best-effort: si falla, el webhook todavía va a cerrar el pago.
    }
  }

  // Limpiar los query params para que un refresh no vuelva a disparar reconcile.
  this.router.navigate([], {
    relativeTo: this.route,
    queryParams: {},
    replaceUrl: true,
  });

  await this.reloadAddOns();
}
```

## Guardrails

- **Idempotente**: el reconcile se puede llamar varias veces sin side effects — el handler devuelve
  el estado terminal si ya se cerró.
- **Best-effort**: si el reconcile falla (red caída, provider inaccesible), el webhook sigue siendo
  el camino autoritativo. No bloquear la UI por error de reconcile.
- **No persistir `paymentId` entre sesiones**: usar `sessionStorage` (muere al cerrar pestaña), no
  `localStorage`. Si el usuario vuelve mucho tiempo después, el webhook ya habrá llegado.
- **Llave por flujo**: `checkout:paymentId:addon`, `checkout:paymentId:renewal`,
  `checkout:paymentId:seats`. No una sola `checkout:paymentId` compartida (si abre dos tabs se
  pisan).
- **Siempre limpiar query params** después de reconciliar — un refresh no debe re-disparar el
  reconcile ni mostrar el banner "success" de nuevo.
- **No mostrar errores de reconcile al usuario** — es un detalle de implementación. Si falla,
  mostrar la lista normalmente, el webhook completará el pago en segundos.

## Deploy

1. Backend desplegado (endpoint ya está).
2. Landing: aplicar los cambios y rebuild.
3. Validar en producción: iniciar un addon checkout con PayPal, volver, confirmar que el banner
   "pending" NO aparece (o aparece y desaparece en 1-2 segundos).
