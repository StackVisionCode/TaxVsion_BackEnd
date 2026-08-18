namespace TaxVision.Documents.Infrastructure.Rendering;

/// <summary>
/// Catálogo de plantillas EMBEBIDAS para el primer slice E2E. La decisión de diseño es "plantillas en
/// BD versionada" (DocumentTemplateVersions); ese aggregate + su seeding llegan en una fase posterior.
/// Mientras tanto, el slice resuelve billing.invoice.v1 desde acá para poder generar de punta a punta
/// sin bloquear en la capa de plantillas. La clave/versión coinciden con lo que se persistirá en BD.
/// </summary>
internal static class EmbeddedDocumentTemplates
{
    public static bool TryGet(string templateKey, int version, out string source)
    {
        if (string.Equals(templateKey, "billing.invoice.v1", StringComparison.OrdinalIgnoreCase) && version == 1)
        {
            source = InvoiceV1;
            return true;
        }

        if (string.Equals(templateKey, "onboarding.receipt.v1", StringComparison.OrdinalIgnoreCase) && version == 1)
        {
            source = OnboardingReceiptV1;
            return true;
        }

        source = string.Empty;
        return false;
    }

    // Liquid (Fluid). Los datos llegan bajo la variable "invoice" (ver ProcessInvoiceGenerationHandler).
    // HTML autocontenido, apto para impresión A4; sin recursos externos (CSP del motor: nada de red/FS).
    private const string InvoiceV1 = """
        <!DOCTYPE html>
        <html lang="es">
        <head>
          <meta charset="utf-8" />
          <title>Factura {{ invoice.number }}</title>
          <style>
            :root { --brand: {{ invoice.brandColor }}; }
            * { box-sizing: border-box; }
            body { font-family: 'Helvetica Neue', Arial, sans-serif; color: #1f2933; margin: 0; padding: 32px; font-size: 12px; }
            .header { display: flex; justify-content: space-between; align-items: flex-start; border-bottom: 2px solid var(--brand); padding-bottom: 16px; }
            .header h1 { margin: 0; font-size: 26px; color: var(--brand); letter-spacing: 1px; }
            .logo { max-height: 56px; max-width: 220px; margin-bottom: 8px; display: block; }
            .meta { text-align: right; font-size: 12px; }
            .meta strong { font-size: 14px; }
            .parties { display: flex; justify-content: space-between; margin: 24px 0; gap: 24px; }
            .party { width: 48%; }
            .party h2 { font-size: 11px; text-transform: uppercase; color: #6b7280; margin: 0 0 6px; letter-spacing: .5px; }
            .party p { margin: 2px 0; }
            table { width: 100%; border-collapse: collapse; margin-top: 8px; }
            th { text-align: left; background: #f3f4f6; padding: 8px; font-size: 11px; text-transform: uppercase; color: #374151; }
            td { padding: 8px; border-bottom: 1px solid #e5e7eb; }
            td.num, th.num { text-align: right; }
            .totals { margin-top: 16px; width: 260px; margin-left: auto; }
            .totals div { display: flex; justify-content: space-between; padding: 4px 0; }
            .totals .grand { border-top: 2px solid var(--brand); margin-top: 6px; padding-top: 8px; font-size: 15px; font-weight: bold; color: var(--brand); }
            .notes { margin-top: 28px; font-size: 11px; color: #6b7280; }
            .footer { margin-top: 40px; text-align: center; font-size: 10px; color: #9ca3af; }
            /* Marca de agua: capa detrás del contenido, rotada, semitransparente. Chromium la imprime
               en el PDF tal cual. El color/texto salen del estado que manda Billing. */
            .watermark { position: fixed; top: 50%; left: 50%; transform: translate(-50%, -50%) rotate(-30deg);
              font-size: 120px; font-weight: 800; letter-spacing: 6px; opacity: .12; z-index: 0;
              pointer-events: none; text-transform: uppercase; }
            .watermark.paid { color: #16a34a; }
            .watermark.overdue { color: #dc2626; }
            .watermark.cancelled { color: #6b7280; }
            body > *:not(.watermark) { position: relative; z-index: 1; }
            .badge { display: inline-block; padding: 3px 10px; border-radius: 999px; font-size: 11px;
              font-weight: 700; text-transform: uppercase; letter-spacing: .5px; }
            .badge.paid { background: #dcfce7; color: #15803d; }
            .badge.overdue { background: #fee2e2; color: #b91c1c; }
            .badge.pending { background: #fef9c3; color: #a16207; }
            .badge.cancelled { background: #f3f4f6; color: #4b5563; }
            .paid-note { margin-top: 16px; color: #15803d; font-weight: 600; font-size: 12px; }
            .receipt { margin-top: 12px; padding: 14px 16px; border: 1px solid #bbf7d0; background: #f0fdf4; border-radius: 8px; }
            .receipt-title { font-size: 12px; font-weight: 700; color: #15803d; margin-bottom: 8px; text-transform: uppercase; letter-spacing: 0.04em; }
            .receipt-row { display: flex; justify-content: space-between; gap: 12px; font-size: 11px; padding: 3px 0; }
            .receipt-row span { color: #6b7280; }
            .receipt-row code { font-family: monospace; font-size: 9.5px; color: #166534; word-break: break-all; text-align: right; }
            .pay { margin-top: 24px; padding: 16px; border: 1px solid #bfdbfe; background: #eff6ff; border-radius: 8px; }
            .pay h3 { margin: 0 0 8px; font-size: 13px; color: #1e40af; }
            .pay { display: flex; gap: 16px; align-items: center; }
            .pay .pay-body { flex: 1; }
            .pay-btn { display: inline-block; background: var(--brand); color: #fff; text-decoration: none;
              padding: 10px 22px; border-radius: 6px; font-weight: 700; font-size: 13px; }
            .pay-url { margin-top: 8px; font-size: 10px; color: #3b82f6; word-break: break-all; }
            .pay-qr { width: 104px; height: 104px; flex-shrink: 0; }
            .pay-qr img { width: 104px; height: 104px; display: block; }
            .pay-qr span { display: block; text-align: center; font-size: 9px; color: #6b7280; margin-top: 2px; }
          </style>
        </head>
        <body>
          {% if invoice.status == "Paid" %}<div class="watermark paid">Pagado</div>
          {% elsif invoice.status == "Overdue" %}<div class="watermark overdue">Vencida</div>
          {% elsif invoice.status == "Cancelled" %}<div class="watermark cancelled">Anulada</div>{% endif %}

          <div class="header">
            <div>
              {% if invoice.logo != "" %}<img class="logo" src="{{ invoice.logo }}" alt="{{ invoice.displayName }}" />{% endif %}
              <h1>FACTURA</h1>
              <p>{{ invoice.displayName }}</p>
            </div>
            <div class="meta">
              <strong>N.º {{ invoice.number }}</strong><br />
              Emisión: {{ invoice.issueDate }}<br />
              {% if invoice.dueDate != "" %}Vencimiento: {{ invoice.dueDate }}<br />{% endif %}
              Ejercicio fiscal: {{ invoice.taxYear }}<br />
              {% if invoice.status == "Paid" %}<span class="badge paid">Pagada</span>
              {% elsif invoice.status == "Overdue" %}<span class="badge overdue">Vencida</span>
              {% elsif invoice.status == "Cancelled" %}<span class="badge cancelled">Anulada</span>
              {% else %}<span class="badge pending">Pendiente</span>{% endif %}
            </div>
          </div>

          <div class="parties">
            <div class="party">
              <h2>Emisor</h2>
              <p><strong>{{ invoice.issuer.name }}</strong></p>
              <p>NIF/RUC: {{ invoice.issuer.taxId }}</p>
              {% if invoice.issuer.address != "" %}<p>{{ invoice.issuer.address }}</p>{% endif %}
            </div>
            <div class="party">
              <h2>Cliente</h2>
              <p><strong>{{ invoice.customer.name }}</strong></p>
              <p>NIF/RUC: {{ invoice.customer.taxId }}</p>
              {% if invoice.customer.address != "" %}<p>{{ invoice.customer.address }}</p>{% endif %}
            </div>
          </div>

          <table>
            <thead>
              <tr>
                <th>Descripción</th>
                <th class="num">Cantidad</th>
                <th class="num">Precio</th>
                <th class="num">Importe</th>
              </tr>
            </thead>
            <tbody>
              {% for line in invoice.lines %}
              <tr>
                <td>{{ line.description }}</td>
                <td class="num">{{ line.quantity }}</td>
                <td class="num">{{ line.unitPrice }} {{ invoice.currency }}</td>
                <td class="num">{{ line.amount }} {{ invoice.currency }}</td>
              </tr>
              {% endfor %}
            </tbody>
          </table>

          <div class="totals">
            <div><span>Subtotal</span><span>{{ invoice.subtotal }} {{ invoice.currency }}</span></div>
            <div><span>Impuestos</span><span>{{ invoice.taxAmount }} {{ invoice.currency }}</span></div>
            {% for adj in invoice.adjustments %}
            <div><span>{{ adj.label }}</span><span>-{{ adj.amount }} {{ invoice.currency }}</span></div>
            {% endfor %}
            <div class="grand"><span>Total</span><span>{{ invoice.total }} {{ invoice.currency }}</span></div>
          </div>

          {% if invoice.settlementType == "FullyCoveredByCode" %}
          <div class="paid-note">✓ Cubierto al 100% por código — no se requirió pago.</div>
          {% endif %}

          {% if invoice.status == "Paid" %}
          <div class="paid-note">✓ Factura pagada{% if invoice.paidDate != "" %} el {{ invoice.paidDate }}{% endif %}. No se requiere ninguna acción.</div>
          {% if invoice.receiptNumber != "" %}
          <div class="receipt">
            <div class="receipt-title">Recibo de pago</div>
            <div class="receipt-row"><span>N.º de recibo</span><strong>{{ invoice.receiptNumber }}</strong></div>
            <div class="receipt-row"><span>Hash de verificación (SHA-256)</span><code>{{ invoice.receiptHash }}</code></div>
          </div>
          {% endif %}
          {% elsif invoice.paymentUrl != "" %}
          <div class="pay">
            <div class="pay-body">
              <h3>Pagar esta factura</h3>
              <a class="pay-btn" href="{{ invoice.paymentUrl }}">Pagar {{ invoice.total }} {{ invoice.currency }}</a>
              <div class="pay-url">{{ invoice.paymentUrl }}</div>
            </div>
            {% if invoice.paymentQr != "" %}<div class="pay-qr"><img src="{{ invoice.paymentQr }}" alt="QR de pago" /><span>Escaneá para pagar</span></div>{% endif %}
          </div>
          {% endif %}

          {% if invoice.notes != "" %}<div class="notes">{{ invoice.notes }}</div>{% endif %}

          <div class="footer">{{ invoice.footer }}</div>
        </body>
        </html>
        """;

    // PayFlow (Fase 10). Datos bajo la variable "receipt" (ver ProcessOnboardingReceiptGenerationHandler).
    // Emisor plataforma fijo (issuer.*) — nunca branding de tenant, no hay tenant todavía.
    private const string OnboardingReceiptV1 = """
        <!DOCTYPE html>
        <html lang="es">
        <head>
          <meta charset="utf-8" />
          <title>Recibo de pago {{ receipt.transactionReferenceMask }}</title>
          <style>
            * { box-sizing: border-box; }
            body { font-family: 'Helvetica Neue', Arial, sans-serif; color: #1f2933; margin: 0; padding: 32px; font-size: 12px; }
            .header { display: flex; justify-content: space-between; align-items: flex-start; border-bottom: 2px solid #2563eb; padding-bottom: 16px; }
            .header h1 { margin: 0; font-size: 24px; color: #2563eb; letter-spacing: 1px; }
            .logo { max-height: 56px; max-width: 220px; margin-bottom: 8px; display: block; }
            .meta { text-align: right; font-size: 12px; }
            .meta strong { font-size: 14px; }
            .issuer { margin: 24px 0; }
            .issuer h2 { font-size: 11px; text-transform: uppercase; color: #6b7280; margin: 0 0 6px; letter-spacing: .5px; }
            .issuer p { margin: 2px 0; }
            .summary { margin-top: 16px; padding: 16px; border: 1px solid #bbf7d0; background: #f0fdf4; border-radius: 8px; }
            .summary-title { font-size: 12px; font-weight: 700; color: #15803d; margin-bottom: 10px; text-transform: uppercase; letter-spacing: .04em; }
            .row { display: flex; justify-content: space-between; gap: 12px; font-size: 12px; padding: 4px 0; }
            .row span:first-child { color: #6b7280; }
            .row code { font-family: monospace; font-size: 10px; color: #166534; }
            .total { margin-top: 16px; width: 260px; margin-left: auto; }
            .total .grand { border-top: 2px solid #2563eb; margin-top: 6px; padding-top: 8px; font-size: 16px; font-weight: bold; color: #2563eb; display: flex; justify-content: space-between; }
            .paid-note { margin-top: 20px; color: #15803d; font-weight: 600; font-size: 12px; }
            .footer { margin-top: 40px; text-align: center; font-size: 10px; color: #9ca3af; }
          </style>
        </head>
        <body>
          <div class="header">
            <div>
              {% if receipt.issuer.logo != "" %}<img class="logo" src="{{ receipt.issuer.logo }}" alt="{{ receipt.issuer.name }}" />{% endif %}
              <h1>RECIBO DE PAGO</h1>
              <p>{{ receipt.issuer.name }}</p>
            </div>
            <div class="meta">
              <strong>Ref. {{ receipt.transactionReferenceMask }}</strong><br />
              Fecha: {{ receipt.paidAt }}
            </div>
          </div>

          <div class="issuer">
            <h2>Emisor</h2>
            <p><strong>{{ receipt.issuer.name }}</strong></p>
            <p>{{ receipt.issuer.taxId }}</p>
            <p>{{ receipt.issuer.addressLine1 }}, {{ receipt.issuer.city }}, {{ receipt.issuer.state }} {{ receipt.issuer.postalCode }}, {{ receipt.issuer.country }}</p>
            <p>{{ receipt.issuer.email }} · {{ receipt.issuer.phone }} · {{ receipt.issuer.website }}</p>
          </div>

          <div class="summary">
            <div class="summary-title">Pagado por</div>
            <div class="row"><span>Nombre</span><span>{{ receipt.payerName }}</span></div>
            <div class="row"><span>Email</span><span>{{ receipt.payerEmail }}</span></div>
            <div class="row"><span>Plan</span><span>{{ receipt.planName }}</span></div>
            {% if receipt.paymentMethodMasked != "" %}<div class="row"><span>Método de pago</span><span>{{ receipt.paymentMethodMasked }}</span></div>{% endif %}
            <div class="row"><span>Referencia de transacción</span><code>{{ receipt.transactionReferenceMask }}</code></div>
          </div>

          <div class="total">
            <div class="grand"><span>Total pagado</span><span>{{ receipt.price }} {{ receipt.currency }}</span></div>
          </div>

          <div class="paid-note">✓ Pago confirmado el {{ receipt.paidAt }}. Este recibo es tu comprobante de pago del onboarding.</div>

          <div class="footer">Documento generado por {{ receipt.issuer.name }} · Ref. {{ receipt.transactionReferenceMask }}</div>
        </body>
        </html>
        """;
}
