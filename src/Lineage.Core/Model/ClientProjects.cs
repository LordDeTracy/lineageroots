using System.Text.Json.Serialization;

namespace Lineage.Core.Model;

public enum ProjectStatus { Open, OnHold, Finished }

/// <summary>
/// A piece of paid research for a client (Advanced mode): the question, the hours worked, what's promised and what's
/// been invoiced. Kept with the family file, beside the research it's about.
/// </summary>
public sealed class ClientProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Client { get; set; } = "";
    /// <summary>Email, phone or address.</summary>
    public string Contact { get; set; } = "";
    /// <summary>The research question agreed with the client.</summary>
    public string Question { get; set; } = "";
    public GenDate Started { get; set; } = GenDate.Empty;
    public GenDate Due { get; set; } = GenDate.Empty;
    public decimal Rate { get; set; }
    /// <summary>"$", "£", "€"…</summary>
    public string Currency { get; set; } = "$";
    /// <summary>The hours agreed (0 = no limit).</summary>
    public double BudgetHours { get; set; }
    public ProjectStatus Status { get; set; }
    public string Note { get; set; } = "";
    /// <summary>The people in the tree the research is about.</summary>
    public List<long> PersonIds { get; set; } = new();
    public List<TimeEntry> Time { get; set; } = new();
    public List<Deliverable> Deliverables { get; set; } = new();
    public List<Invoice> Invoices { get; set; } = new();

    [JsonIgnore] public double Hours => Time.Sum(t => t.Hours);
    [JsonIgnore] public double UnbilledHours => Time.Where(t => !t.Billed).Sum(t => t.Hours);
    [JsonIgnore] public decimal Unbilled => Math.Round((decimal)UnbilledHours * Rate, 2);
    [JsonIgnore] public decimal Invoiced => Invoices.Sum(i => i.Amount);
    [JsonIgnore] public decimal Outstanding => Invoices.Where(i => !i.Paid).Sum(i => i.Amount);
    public string Money(decimal amount) => $"{Currency}{amount:N2}";

    /// <summary>Bills every unbilled hour on a new invoice ("INV-2026-003"); null when there's nothing to bill.</summary>
    public Invoice? InvoiceUnbilled(DateTime today, string number)
    {
        var lines = Time.Where(t => !t.Billed && t.Hours > 0).ToList();
        if (lines.Count == 0) return null;
        var invoice = new Invoice
        {
            Number = number,
            Date = new GenDate { Day = today.Day, Month = today.Month, Year = today.Year },
            Amount = Math.Round((decimal)lines.Sum(t => t.Hours) * Rate, 2),
            Lines = lines.Select(t => new TimeEntry { Date = t.Date, Hours = t.Hours, Work = t.Work, Billed = true }).ToList(),
        };
        foreach (var t in lines) t.Billed = true;
        Invoices.Add(invoice);
        return invoice;
    }
}

public sealed class TimeEntry
{
    public GenDate Date { get; set; } = GenDate.Empty;
    public double Hours { get; set; }
    public string Work { get; set; } = "";
    public bool Billed { get; set; }
}

public sealed class Deliverable
{
    public string What { get; set; } = "";
    public GenDate Due { get; set; } = GenDate.Empty;
    public bool Done { get; set; }
}

public sealed class Invoice
{
    public string Number { get; set; } = "";
    public GenDate Date { get; set; } = GenDate.Empty;
    public decimal Amount { get; set; }
    public bool Paid { get; set; }
    /// <summary>The hours the invoice is for, as they were when it was made.</summary>
    public List<TimeEntry> Lines { get; set; } = new();
}
