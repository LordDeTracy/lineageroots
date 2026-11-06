using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

public sealed partial class TimeRow : ObservableObject
{
    [ObservableProperty] private string _date = "";
    [ObservableProperty] private string _hours = "";
    [ObservableProperty] private string _work = "";
    [ObservableProperty] private bool _billed;
}

public sealed partial class DeliverableRow : ObservableObject
{
    [ObservableProperty] private string _what = "";
    [ObservableProperty] private string _due = "";
    [ObservableProperty] private bool _done;
}

public sealed partial class InvoiceRow : ObservableObject
{
    public InvoiceRow(Invoice invoice, string amount) { Invoice = invoice; Amount = amount; _paid = invoice.Paid; }
    public Invoice Invoice { get; }
    public string Number => Invoice.Number;
    public string Date => Display.Date(Invoice.Date);
    public string Amount { get; }
    [ObservableProperty] private bool _paid;
}

/// <summary>
/// Edits a client project: the client and question, the rate and budget, hours worked, what's promised, and invoices
/// (made from the unbilled hours). Works on a copy; <see cref="Apply"/> writes it back.
/// </summary>
public sealed partial class ProjectEditorViewModel : ObservableObject
{
    private readonly ClientProject _draft;

    public ProjectEditorViewModel(ClientProject project, bool isNew)
    {
        IsNew = isNew;
        _draft = Lineage.Core.Storage.LineageStore.Copy(project);
        _client = project.Client;
        _contact = project.Contact;
        _question = project.Question;
        _started = Display.Date(project.Started);
        _due = Display.Date(project.Due);
        _rate = project.Rate > 0 ? project.Rate.ToString("0.##", CultureInfo.CurrentCulture) : "";
        _currency = project.Currency;
        _budget = project.BudgetHours > 0 ? project.BudgetHours.ToString("0.#", CultureInfo.CurrentCulture) : "";
        _statusIndex = (int)project.Status;
        _note = project.Note;
        foreach (var t in project.Time)
            Time.Add(new TimeRow { Date = Display.Date(t.Date), Hours = t.Hours.ToString("0.##", CultureInfo.CurrentCulture), Work = t.Work, Billed = t.Billed });
        foreach (var d in project.Deliverables) Deliverables.Add(new DeliverableRow { What = d.What, Due = Display.Date(d.Due), Done = d.Done });
        foreach (var i in _draft.Invoices) Invoices.Add(new InvoiceRow(i, _draft.Money(i.Amount)));
        Time.CollectionChanged += (_, _) => Totals();
        Totals();
    }

    public bool IsNew { get; }
    public string Title => IsNew ? "New client project" : "Client project";
    public static string[] Statuses { get; } = { "Open", "On hold", "Finished" };

    [ObservableProperty] private string _client;
    [ObservableProperty] private string _contact;
    [ObservableProperty] private string _question;
    [ObservableProperty] private string _started;
    [ObservableProperty] private string _due;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TotalsText))] private string _rate;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TotalsText))] private string _currency;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TotalsText))] private string _budget;
    [ObservableProperty] private int _statusIndex;
    [ObservableProperty] private string _note;
    [ObservableProperty] private string _totalsText = "";

    public ObservableCollection<TimeRow> Time { get; } = new();
    public ObservableCollection<DeliverableRow> Deliverables { get; } = new();
    public ObservableCollection<InvoiceRow> Invoices { get; } = new();
    public bool IsValid => Client.Trim().Length > 0;

    /// <summary>The invoice made by <see cref="InvoiceUnbilled"/>, for the caller to save as a document.</summary>
    public Invoice? NewInvoice { get; private set; }

    private static double Hours(string s) => double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var h) ? h : 0;
    private static decimal Money(string s) => decimal.TryParse(s.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var m) ? m : 0;

    [RelayCommand]
    private void AddTime()
    {
        var row = new TimeRow { Date = Display.Date(GenDate.Parse(DateTime.Today.ToString("d MMM yyyy", CultureInfo.InvariantCulture))) };
        row.PropertyChanged += (_, _) => Totals();
        Time.Add(row);
    }

    [RelayCommand] private void RemoveTime(TimeRow row) => Time.Remove(row);
    [RelayCommand] private void AddDeliverable() => Deliverables.Add(new DeliverableRow());
    [RelayCommand] private void RemoveDeliverable(DeliverableRow row) => Deliverables.Remove(row);

    private void Totals()
    {
        foreach (var row in Time) { row.PropertyChanged -= OnRow; row.PropertyChanged += OnRow; }
        var hours = Time.Sum(t => Hours(t.Hours));
        var unbilled = Time.Where(t => !t.Billed).Sum(t => Hours(t.Hours));
        var budget = Hours(Budget);
        var money = Currency.Trim().Length > 0 ? Currency.Trim() : "$";
        TotalsText = Display.Join($"{hours:0.##} hours worked" + (budget > 0 ? $" of {budget:0.#}" : ""),
            unbilled > 0 && Money(Rate) > 0 ? $"{money}{(decimal)unbilled * Money(Rate):N2} not yet invoiced" : "",
            budget > 0 && hours > budget ? "over budget" : "");
    }

    private void OnRow(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Totals();
    partial void OnRateChanged(string value) => Totals();
    partial void OnBudgetChanged(string value) => Totals();
    partial void OnCurrencyChanged(string value) => Totals();

    /// <summary>Puts every unbilled hour on a new invoice, numbered on from the client's last one.</summary>
    [RelayCommand]
    private void InvoiceUnbilled()
    {
        Apply(_draft);
        var number = $"INV-{DateTime.Today:yyyy}-{_draft.Invoices.Count + 1:000}";
        if (_draft.InvoiceUnbilled(DateTime.Today, number) is not { } invoice) return;
        NewInvoice = invoice;
        foreach (var row in Time) row.Billed = true;
        Invoices.Add(new InvoiceRow(invoice, _draft.Money(invoice.Amount)));
        Totals();
    }

    public void Apply(ClientProject p)
    {
        p.Client = Client.Trim();
        p.Contact = Contact.Trim();
        p.Question = Question.Trim();
        p.Started = GenDate.Parse(Started);
        p.Due = GenDate.Parse(Due);
        p.Rate = Money(Rate);
        p.Currency = Currency.Trim().Length > 0 ? Currency.Trim() : "$";
        p.BudgetHours = Hours(Budget);
        p.Status = (ProjectStatus)Math.Clamp(StatusIndex, 0, 2);
        p.Note = Note;
        p.Time = Time.Where(t => Hours(t.Hours) > 0 || t.Work.Trim().Length > 0)
            .Select(t => new TimeEntry { Date = GenDate.Parse(t.Date), Hours = Hours(t.Hours), Work = t.Work.Trim(), Billed = t.Billed }).ToList();
        p.Deliverables = Deliverables.Where(d => d.What.Trim().Length > 0)
            .Select(d => new Deliverable { What = d.What.Trim(), Due = GenDate.Parse(d.Due), Done = d.Done }).ToList();
        foreach (var row in Invoices) row.Invoice.Paid = row.Paid;
        p.Invoices = Invoices.Select(r => r.Invoice).ToList();
    }
}
