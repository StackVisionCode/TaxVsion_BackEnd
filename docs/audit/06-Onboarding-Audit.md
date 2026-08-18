# Auditoría de Onboarding

Auth es dueño del aggregate y la saga. Estados y métodos explícitos controlan email, plan, checkout, payment/fully-covered, registration token, provisioning y completion. Jobs de retry/polling apoyan recuperación.

### ONB-001 — éxito antes de asentamiento financiero

**HIGH/P1/Large.** `OnboardingSuccessCompleter` habilita el camino de registro y encola finalize; Billing trabaja después por evento. No hay ACK `InvoiceCreated` como condición. Caso real: Billing caído durante C; usuario continúa y invoice aparece tarde o nunca si el evento es poison.

### ONB-002 — respuesta de cero usa PaymentId vacío

**LOW/P3/Small.** `StartOnboardingCheckoutResponse.PaymentId` no es nullable y devuelve `Guid.Empty`. El dominio usa null correctamente, pero clientes pueden confundir empty con payment. Versionar contrato y hacerlo nullable/discriminated union.

### ONB-003 — retry de checkout con reservas expiradas

**MEDIUM/P2/Medium.** Si `CodeReservations.Count>0`, el handler no recotiza. Una sesión/reintento posterior puede usar reservas expiradas; commit fallará después del éxito o cobertura. Validar vigencia y estado antes de reuse.

### ONB-004 — compensación de cancelación incompleta por stack

**HIGH/P1/Medium.** Debe garantizarse cancelación de todas las reservas ante cancel, timeout y fallo de pago; el camino de creación parcial no registra todas en Auth hasta terminar el batch.

