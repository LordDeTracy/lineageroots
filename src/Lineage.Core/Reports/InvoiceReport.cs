using System.Globalization;
using Lineage.Core.Model;

namespace Lineage.Core.Reports;

public static partial class Narrative
{
    /// <summary>An invoice for a client project: who it's for, the work and hours, and the total.</summary>
    public static ReportDocument InvoiceDocument(ClientProject project, Invoice invoice, string from)
    {
        var doc = new ReportDocument
        {
            Title = $"Invoice {invoice.Number}",
            Subtitle = Join(" · ", ReportText.Date(invoice.Date), project.Client),
            Author = from,
        };
        doc.Add(new Paragraph(new[] { Run.B("To: "), new Run(project.Client) }));
        if (project.Contact.Trim().Length > 0) doc.Add(new Paragraph(project.Contact.Trim(), ParagraphStyle.Muted));
        if (project.Question.Trim().Length > 0) doc.Add(new Paragraph(new[] { Run.B("For: "), new Run(project.Question.Trim()) }));
        var rows = invoice.Lines.Select(t => (IReadOnlyList<Cell>)new Cell[]
        {
            ReportText.Date(t.Date), t.Work, t.Hours.ToString("0.##", CultureInfo.CurrentCulture),
            project.Money(Math.Round((decimal)t.Hours * project.Rate, 2)),
        }).ToList();
        rows.Add(new Cell[]
        {
            "", new(new[] { Run.B("Total") }), new(new[] { Run.B(invoice.Lines.Sum(t => t.Hours).ToString("0.##", CultureInfo.CurrentCulture)) }),
            new(new[] { Run.B(project.Money(invoice.Amount)) }),
        });
        doc.Add(new Table(new[] { "Date", "Work", "Hours", "Amount" }, rows, new[] { 1.2f, 4f, 0.8f, 1.2f }));
        doc.Add(new Paragraph($"Rate: {project.Money(project.Rate)} an hour.", ParagraphStyle.Muted));
        return doc;
    }
}
