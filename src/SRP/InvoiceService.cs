namespace SolidPrinciples.SRP;

// ---------------------------------------------------------------------------
// BEFORE — one class, three reasons to change.
//
// Tax rules, persistence and email formatting are fused together. A change to
// the tax rate forces you to re-test the email, and the tax math itself cannot
// be unit tested without a database and an SMTP server.
// ---------------------------------------------------------------------------

public class Invoice
{
    public string Number { get; set; } = "";
    public string CustomerEmail { get; set; } = "";
    public List<InvoiceItem> Items { get; } = new();
    public decimal Subtotal { get; private set; }
    public decimal Tax { get; private set; }

    public void Apply(InvoiceTotals totals)
    {
        Subtotal = totals.Subtotal;
        Tax = totals.Tax;
    }
}

public record InvoiceItem(string Description, decimal Price, int Quantity);

public record InvoiceTotals(decimal Subtotal, decimal Tax)
{
    public decimal Total => Subtotal + Tax;
}

public class InvoiceServiceBefore
{
    private readonly string _connectionString = "Server=prod;Database=billing";

    public void ProcessInvoice(Invoice invoice)
    {
        // 1. Business rule
        var subtotal = invoice.Items.Sum(i => i.Price * i.Quantity);
        var tax = subtotal * 0.18m;
        invoice.Apply(new InvoiceTotals(subtotal, tax));

        // 2. Persistence
        SaveToDatabase(invoice);

        // 3. Presentation
        SendEmail(invoice);
    }

    private void SaveToDatabase(Invoice invoice)
    {
        // new SqlConnection(_connectionString) ...
        _ = _connectionString;
    }

    private void SendEmail(Invoice invoice)
    {
        var body = $"<h1>Invoice {invoice.Number}</h1><p>Total: {invoice.Subtotal + invoice.Tax:C}</p>";
        // new SmtpClient("smtp.example.com").Send(...)
        _ = body;
    }
}

// ---------------------------------------------------------------------------
// AFTER — each responsibility owns its reason to change.
// ---------------------------------------------------------------------------

/// <summary>Pure business rule. No I/O, no infrastructure, trivially testable.</summary>
public class InvoiceCalculator
{
    private const decimal TaxRate = 0.18m;

    public InvoiceTotals Calculate(Invoice invoice)
    {
        var subtotal = invoice.Items.Sum(i => i.Price * i.Quantity);
        return new InvoiceTotals(subtotal, subtotal * TaxRate);
    }
}

public interface IInvoiceRepository
{
    Task SaveAsync(Invoice invoice, CancellationToken ct = default);
}

public interface IInvoiceNotifier
{
    Task SendAsync(Invoice invoice, CancellationToken ct = default);
}

/// <summary>Coordinates the work. Decides nothing.</summary>
public class InvoiceService
{
    private readonly InvoiceCalculator _calculator;
    private readonly IInvoiceRepository _repository;
    private readonly IInvoiceNotifier _notifier;

    public InvoiceService(
        InvoiceCalculator calculator,
        IInvoiceRepository repository,
        IInvoiceNotifier notifier)
    {
        _calculator = calculator;
        _repository = repository;
        _notifier = notifier;
    }

    public async Task ProcessAsync(Invoice invoice, CancellationToken ct = default)
    {
        invoice.Apply(_calculator.Calculate(invoice));
        await _repository.SaveAsync(invoice, ct);
        await _notifier.SendAsync(invoice, ct);
    }
}
