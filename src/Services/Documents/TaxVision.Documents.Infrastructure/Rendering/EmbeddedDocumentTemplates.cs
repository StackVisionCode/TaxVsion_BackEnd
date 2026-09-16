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
    // Emisor = la plataforma (issuer.*, config PlatformIssuer). El logo (issuer.logo) es el system header
    // logo que administra el PlatformAdmin en Scribe — la misma marca que usan los correos.
    private const string OnboardingReceiptV1 = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <title>Payment receipt {{ receipt.transactionReferenceMask }}</title>
          <style>
            :root {
              --brand: #0074d4; --brand-dark: #0a2540; --ink: #1f2933; --muted: #64748b;
              --line: #e6eaf0; --ok: #15803d; --ok-bg: #f0fdf4; --ok-border: #bbf7d0;
            }
            * { box-sizing: border-box; }
            body { font-family: 'Helvetica Neue', Arial, sans-serif; color: var(--ink); margin: 0; padding: 40px 44px; font-size: 12px; -webkit-print-color-adjust: exact; print-color-adjust: exact; }

            .top { display: flex; justify-content: space-between; align-items: flex-start; padding-bottom: 20px; }
            .brand { display: flex; align-items: center; gap: 12px; }
            .brand .logo { max-height: 44px; max-width: 180px; display: block; }
            .brand .wordmark { font-size: 18px; font-weight: 800; letter-spacing: .2px; color: var(--brand-dark); line-height: 1.1; }
            .doc-meta { text-align: right; }
            .doc-meta .kicker { font-size: 10px; font-weight: 700; letter-spacing: .16em; text-transform: uppercase; color: var(--brand); }
            .doc-meta h1 { margin: 2px 0 8px; font-size: 22px; font-weight: 800; color: var(--brand-dark); letter-spacing: .3px; }
            .doc-meta .ref { font-size: 11px; color: var(--muted); }
            .doc-meta .ref b { color: var(--ink); font-weight: 600; }
            .accent { height: 4px; border-radius: 3px; background: linear-gradient(90deg, var(--brand) 0%, #4aa3e8 60%, #bfe0f7 100%); }

            .paid { margin: 22px 0 26px; display: flex; align-items: center; gap: 12px; padding: 14px 18px; background: var(--ok-bg); border: 1px solid var(--ok-border); border-radius: 12px; }
            .paid .check { width: 30px; height: 30px; border-radius: 999px; background: var(--ok); color: #fff; display: flex; align-items: center; justify-content: center; font-size: 16px; font-weight: 800; flex-shrink: 0; }
            .paid .txt strong { display: block; color: var(--ok); font-size: 13px; }
            .paid .txt span { color: #3f6b4f; font-size: 11px; }
            .paid .amt { margin-left: auto; text-align: right; }
            .paid .amt .n { font-size: 22px; font-weight: 800; color: var(--brand-dark); letter-spacing: .2px; }
            .paid .amt .c { font-size: 10.5px; color: var(--muted); text-transform: uppercase; letter-spacing: .08em; }

            .parties { display: flex; gap: 28px; margin-bottom: 26px; }
            .party { flex: 1; }
            .party h2 { font-size: 9.5px; text-transform: uppercase; letter-spacing: .1em; color: var(--muted); margin: 0 0 8px; font-weight: 700; }
            .party .name { font-size: 13px; font-weight: 700; color: var(--brand-dark); }
            .party p { margin: 3px 0; color: #47546a; line-height: 1.5; }
            .party .sub { color: var(--muted); font-size: 10.5px; }

            table { width: 100%; border-collapse: collapse; margin-bottom: 4px; }
            thead th { text-align: left; font-size: 9.5px; text-transform: uppercase; letter-spacing: .08em; color: var(--muted); font-weight: 700; padding: 0 0 10px; border-bottom: 1.5px solid var(--brand-dark); }
            thead th.num { text-align: right; }
            tbody td { padding: 14px 0; border-bottom: 1px solid var(--line); vertical-align: top; }
            tbody td.num { text-align: right; font-variant-numeric: tabular-nums; }
            .item-name { font-weight: 700; color: var(--brand-dark); font-size: 12.5px; }
            .item-desc { color: var(--muted); font-size: 10.5px; margin-top: 3px; }

            .totals { width: 260px; margin-left: auto; margin-top: 14px; }
            .totals .r { display: flex; justify-content: space-between; padding: 5px 0; color: #47546a; }
            .totals .grand { margin-top: 8px; padding-top: 12px; border-top: 2px solid var(--brand-dark); font-size: 15px; font-weight: 800; color: var(--brand-dark); }
            .totals .grand .g { font-variant-numeric: tabular-nums; }

            .details { margin-top: 30px; border: 1px solid var(--line); border-radius: 12px; overflow: hidden; }
            .details .h { background: #f7f9fc; padding: 10px 16px; font-size: 9.5px; text-transform: uppercase; letter-spacing: .1em; color: var(--muted); font-weight: 700; border-bottom: 1px solid var(--line); }
            .details .grid { display: flex; flex-wrap: wrap; }
            .details .cell { width: 50%; padding: 12px 16px; border-bottom: 1px solid var(--line); }
            .details .cell:nth-child(odd) { border-right: 1px solid var(--line); }
            .details .cell .k { font-size: 10px; color: var(--muted); margin-bottom: 3px; }
            .details .cell .v { font-size: 12px; color: var(--ink); font-weight: 600; }
            .details .cell .v code { font-family: 'SF Mono', Consolas, monospace; font-size: 10.5px; color: var(--brand-dark); word-break: break-all; }

            .foot { margin-top: 36px; padding-top: 18px; border-top: 1px solid var(--line); display: flex; justify-content: space-between; align-items: flex-end; gap: 24px; }
            .foot .legal { font-size: 9.5px; color: var(--muted); line-height: 1.6; max-width: 64%; }
            .foot .legal b { color: #47546a; }
            .foot .verify { text-align: right; font-size: 9px; color: var(--muted); }
            .foot .verify .hash { font-family: 'SF Mono', Consolas, monospace; color: #94a3b8; word-break: break-all; max-width: 190px; display: inline-block; }
          </style>
        </head>
        <body>
          <div class="top">
            <div class="brand">
              {% if receipt.issuer.logo != "" %}<img class="logo" src="{{ receipt.issuer.logo }}" alt="{{ receipt.issuer.name }}" />{% endif %}
              <div class="wordmark">{{ receipt.issuer.name }}</div>
            </div>
            <div class="doc-meta">
              <div class="kicker">Payment receipt</div>
              <h1>PAID</h1>
              <div class="ref"><b>Ref.</b> {{ receipt.transactionReferenceMask }}</div>
              <div class="ref"><b>Date</b> {{ receipt.paidAt }}</div>
            </div>
          </div>
          <div class="accent"></div>

          <div class="paid">
            <div class="check">&#10003;</div>
            <div class="txt">
              <strong>Payment confirmed</strong>
              <span>This document is your official proof of payment for your onboarding.</span>
            </div>
            <div class="amt">
              <div class="n">{{ receipt.price }}</div>
              <div class="c">{{ receipt.currency }}</div>
            </div>
          </div>

          <div class="parties">
            <div class="party">
              <h2>Billed to</h2>
              <div class="name">{{ receipt.payerName }}</div>
              <p>{{ receipt.payerEmail }}</p>
              <p class="sub">Onboarding customer</p>
            </div>
            <div class="party">
              <h2>Issued by</h2>
              <div class="name">{{ receipt.issuer.name }}</div>
              {% if receipt.issuer.taxId != "" %}<p>{{ receipt.issuer.taxId }}</p>{% endif %}
              {% if receipt.issuer.addressLine1 != "" %}<p>{{ receipt.issuer.addressLine1 }}, {{ receipt.issuer.city }}, {{ receipt.issuer.state }} {{ receipt.issuer.postalCode }}, {{ receipt.issuer.country }}</p>{% endif %}
              <p class="sub">{{ receipt.issuer.email }}{% if receipt.issuer.website != "" %} &middot; {{ receipt.issuer.website }}{% endif %}</p>
            </div>
          </div>

          <table>
            <thead>
              <tr>
                <th>Description</th>
                <th class="num">Amount</th>
              </tr>
            </thead>
            <tbody>
              <tr>
                <td>
                  <div class="item-name">{{ receipt.planName }} Plan &mdash; {{ receipt.issuer.name }} subscription</div>
                  <div class="item-desc">Office activation &middot; full platform access</div>
                </td>
                <td class="num">{{ receipt.price }} {{ receipt.currency }}</td>
              </tr>
            </tbody>
          </table>

          <div class="totals">
            <div class="r"><span>Subtotal</span><span>{{ receipt.price }} {{ receipt.currency }}</span></div>
            <div class="r grand"><span>Total paid</span><span class="g">{{ receipt.price }} {{ receipt.currency }}</span></div>
          </div>

          <div class="details">
            <div class="h">Payment details</div>
            <div class="grid">
              {% if receipt.paymentMethodMasked != "" %}<div class="cell"><div class="k">Payment method</div><div class="v">{{ receipt.paymentMethodMasked }}</div></div>{% endif %}
              <div class="cell"><div class="k">Payment date</div><div class="v">{{ receipt.paidAt }}</div></div>
              <div class="cell"><div class="k">Transaction reference</div><div class="v"><code>{{ receipt.transactionReferenceMask }}</code></div></div>
              <div class="cell"><div class="k">Status</div><div class="v" style="color:#15803d;">Confirmed</div></div>
            </div>
          </div>

          <div class="foot">
            <div class="legal">
              <b>{{ receipt.issuer.name }}</b> issues this receipt for a payment that has already been confirmed. No further action is required. Please keep this document for your records.{% if receipt.issuer.email != "" %} Questions? {{ receipt.issuer.email }}.{% endif %}
            </div>
            <div class="verify">
              Transaction ref.<br />
              <span class="hash">{{ receipt.transactionReferenceMask }}</span>
            </div>
          </div>
        </body>
        </html>
        """;
}
