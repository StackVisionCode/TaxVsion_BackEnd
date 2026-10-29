using TaxVision.Tasks.ClientForms.Application.Actions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Insights;

/// <summary>
/// Arma la respuesta a partir de los agregados de la base: tasas, medianas (desde el histograma),
/// embudo, serie completa con ceros y salud de automatizaciones. Sin acceso a datos.
/// </summary>
public static class FormInsightsAssembler
{
    public const int TopFailureReasons = 5;
    public const string UnknownFailureCode = "unknown";
    public const string PdfFailedCode = "generation_failed";
    public const string SignatureFailedCode = "request_failed";

    public static FormInsightsSummaryResponse Summary(FormInsightsRawSummary raw, FormInsightsWindow window)
    {
        var current = raw.Cohort.Aggregate(FormInsightsCohortCounts.Zero, (sum, item) => sum.Plus(item.Current));
        var previous = raw.Cohort.Aggregate(FormInsightsCohortCounts.Zero, (sum, item) => sum.Plus(item.Previous));
        return new(
            new(window.Days, window.FromUtc, window.ToUtc, window.PreviousFromUtc, window.Granularity),
            Kpis(current, Median(raw.Durations.Select(item => (item.Minutes, item.Current)))),
            Kpis(previous, Median(raw.Durations.Select(item => (item.Minutes, item.Previous)))),
            raw.OverdueOpen,
            Funnel(current),
            Trend(raw.SentByDay, raw.CompletedByDay, window),
            Split(raw.Cohort),
            Automation(raw),
            FailureReasons(raw),
            Signatures(raw.Signatures),
            raw.Templates,
            raw.HasAnyRequests
        );
    }

    public static IReadOnlyList<FormInsightsTemplateRowResponse> Templates(FormInsightsRawTemplatePage raw)
    {
        var durations = raw.Durations.ToLookup(item => item.TemplateId);
        return raw
            .Rows.Select(row => new FormInsightsTemplateRowResponse(
                row.TemplateId,
                raw.Names.GetValueOrDefault(row.TemplateId, "Form"),
                row.Sent,
                row.Completed,
                Rate(row.Completed, row.Sent),
                Median(durations[row.TemplateId].Select(item => (item.Minutes, item.Count))),
                raw.Returns.GetValueOrDefault(row.TemplateId)
            ))
            .ToArray();
    }

    /// <summary>Mediana exacta a la resolución del bucket: promedio de los dos centrales si n es par.</summary>
    public static int? Median(IEnumerable<(int Minutes, int Count)> histogram)
    {
        var buckets = histogram.Where(item => item.Count > 0).OrderBy(item => item.Minutes).ToArray();
        var total = buckets.Sum(item => item.Count);
        if (total == 0)
            return null;
        var lower = ValueAt(buckets, (total - 1) / 2);
        var upper = ValueAt(buckets, total / 2);
        return (int)Math.Round((lower + upper) / 2.0, MidpointRounding.AwayFromZero);
    }

    public static double? Rate(int part, int whole) => whole <= 0 ? null : Math.Round((double)part / whole, 4);

    private static int ValueAt((int Minutes, int Count)[] buckets, int position)
    {
        var seen = 0;
        foreach (var (minutes, count) in buckets)
        {
            seen += count;
            if (position < seen)
                return minutes;
        }
        return buckets[^1].Minutes;
    }

    private static FormInsightsKpisResponse Kpis(FormInsightsCohortCounts counts, int? medianMinutes) =>
        new(
            counts.Sent,
            counts.Started,
            counts.Submitted,
            counts.Completed,
            counts.Cancelled,
            Rate(counts.Completed, counts.Sent),
            medianMinutes,
            counts.Completed == 0 ? null : Math.Round((double)counts.CompletedReminders / counts.Completed, 2)
        );

    private static IReadOnlyList<FormInsightsFunnelStepResponse> Funnel(FormInsightsCohortCounts counts) =>
        [
            new(FormInsightsFunnelKeys.Sent, counts.Sent, null),
            new(FormInsightsFunnelKeys.Started, counts.Started, Rate(counts.Started, counts.Sent)),
            new(FormInsightsFunnelKeys.Submitted, counts.Submitted, Rate(counts.Submitted, counts.Started)),
            new(FormInsightsFunnelKeys.Completed, counts.Completed, Rate(counts.Completed, counts.Submitted)),
        ];

    private static IReadOnlyList<FormInsightsTrendPointResponse> Trend(
        IReadOnlyList<FormInsightsDayCount> sent,
        IReadOnlyList<FormInsightsDayCount> completed,
        FormInsightsWindow window
    )
    {
        var sentBuckets = Bucketize(sent, window);
        var completedBuckets = Bucketize(completed, window);
        return Enumerable
            .Range(0, window.BucketCount)
            .Select(index => new FormInsightsTrendPointResponse(
                window.FirstLocalDay.AddDays(index * window.BucketDays),
                sentBuckets[index],
                completedBuckets[index]
            ))
            .ToArray();
    }

    private static int[] Bucketize(IReadOnlyList<FormInsightsDayCount> days, FormInsightsWindow window)
    {
        var buckets = new int[window.BucketCount];
        foreach (var day in days)
        {
            var offset = DateOnly.FromDateTime(day.Day).DayNumber - window.FirstLocalDay.DayNumber;
            var index = offset < 0 ? -1 : offset / window.BucketDays;
            if (index >= 0 && index < buckets.Length)
                buckets[index] += day.Count;
        }
        return buckets;
    }

    private static IReadOnlyList<FormInsightsParticipantSplitResponse> Split(
        IReadOnlyList<FormInsightsCohortTally> cohort
    ) =>
        new[] { FormParticipantKind.Customer, FormParticipantKind.External }
            .Select(kind =>
            {
                var counts = cohort
                    .Where(item => item.Kind == kind)
                    .Aggregate(FormInsightsCohortCounts.Zero, (sum, item) => sum.Plus(item.Current));
                return new FormInsightsParticipantSplitResponse(kind, counts.Sent, counts.Completed);
            })
            .ToArray();

    private static IReadOnlyList<FormInsightsAutomationResponse> Automation(FormInsightsRawSummary raw)
    {
        int Runs(string type, FormActionRunStatus status) =>
            raw.Runs.Where(item => item.ActionType == type && item.Status == status).Sum(item => item.Count);
        FormInsightsAutomationResponse ActionRow(string type) =>
            new(
                type,
                Runs(type, FormActionRunStatus.Completed),
                Runs(type, FormActionRunStatus.Failed),
                Runs(type, FormActionRunStatus.Rejected),
                Runs(type, FormActionRunStatus.Skipped),
                Runs(type, FormActionRunStatus.Requested)
            );
        int Pdf(FormRevisionPdfStatus status) => raw.Pdfs.Where(item => item.Status == status).Sum(item => item.Count);
        int Sig(Func<FormRevisionSignatureStatus, bool> match) =>
            raw.Signatures.Where(item => match(item.Status)).Sum(item => item.Count);
        return
        [
            ActionRow(FormActionNames.CreateCustomer),
            ActionRow(FormActionNames.UpdateCustomerProfile),
            new(
                FormInsightsAutomationKinds.SummaryPdf,
                Pdf(FormRevisionPdfStatus.Ready),
                Pdf(FormRevisionPdfStatus.Failed),
                0,
                0,
                Pdf(FormRevisionPdfStatus.Requested)
            ),
            // La automatización de firma cumple al entregar la solicitud; lo que decida el firmante va aparte.
            new(
                FormInsightsAutomationKinds.Signature,
                Sig(status =>
                    status is not (FormRevisionSignatureStatus.Requesting or FormRevisionSignatureStatus.Failed)
                ),
                Sig(status => status == FormRevisionSignatureStatus.Failed),
                0,
                0,
                Sig(status => status == FormRevisionSignatureStatus.Requesting)
            ),
        ];
    }

    private static IReadOnlyList<FormInsightsFailureReasonResponse> FailureReasons(FormInsightsRawSummary raw)
    {
        var runs = raw
            .Failures.GroupBy(item => (item.ActionType, Code: item.Code ?? UnknownFailureCode))
            .Select(group => new FormInsightsFailureReasonResponse(
                group.Key.ActionType,
                group.Key.Code,
                group.Sum(item => item.Count)
            ));
        var pdf = raw.Pdfs.Where(item => item.Status == FormRevisionPdfStatus.Failed).Sum(item => item.Count);
        var signature = raw
            .Signatures.Where(item => item.Status == FormRevisionSignatureStatus.Failed)
            .Sum(item => item.Count);
        return runs.Append(new(FormInsightsAutomationKinds.SummaryPdf, PdfFailedCode, pdf))
            .Append(new(FormInsightsAutomationKinds.Signature, SignatureFailedCode, signature))
            .Where(item => item.Count > 0)
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .Take(TopFailureReasons)
            .ToArray();
    }

    private static FormInsightsSignaturesResponse Signatures(IReadOnlyList<FormInsightsSignatureTally> tallies)
    {
        int Count(params FormRevisionSignatureStatus[] statuses) =>
            tallies.Where(item => statuses.Contains(item.Status)).Sum(item => item.Count);
        return new(
            tallies.Where(item => item.Status != FormRevisionSignatureStatus.None).Sum(item => item.Count),
            Count(
                FormRevisionSignatureStatus.Signed,
                FormRevisionSignatureStatus.Completed,
                FormRevisionSignatureStatus.Sealed
            ),
            Count(FormRevisionSignatureStatus.Requesting, FormRevisionSignatureStatus.Sent),
            Count(FormRevisionSignatureStatus.Declined),
            Count(FormRevisionSignatureStatus.Expired),
            Count(FormRevisionSignatureStatus.Canceled),
            Count(FormRevisionSignatureStatus.Failed)
        );
    }
}
