namespace SolidPrinciples.DIP;

// ---------------------------------------------------------------------------
// BEFORE — the business rule depends on three concrete external systems,
// each with a hardcoded endpoint. Untestable without production infrastructure.
// ---------------------------------------------------------------------------

public class Order
{
    public int Id { get; init; }
    public string CustomerEmail { get; init; } = "";
    public string Status { get; private set; } = "pending";

    public void MarkPlaced() => Status = "placed";
}

public class OrderServiceBefore
{
    public async Task PlaceOrderAsync(Order order)
    {
        order.MarkPlaced();

        // Directly coupled to concrete infrastructure.
        // using var db = new SqlConnection("Server=prod;Database=orders;...");
        await Task.Delay(1);

        // var smtp = new SmtpClient("smtp.prod.example.com");
        await Task.Delay(1);

        // var client = new HttpClient();
        // await client.PostAsync("https://api.tax-authority.gov/invoice", ...);
        await Task.Delay(1);
    }
}

// ---------------------------------------------------------------------------
// AFTER — the domain declares the contracts it needs; infrastructure implements.
//
// Note where the interfaces live: with the consumer, expressed in domain terms.
// If the persistence layer dictated them, the dependency would not be inverted.
// ---------------------------------------------------------------------------

public interface IOrderRepository
{
    Task SaveAsync(Order order, CancellationToken ct = default);
}

public interface INotificationSender
{
    Task SendOrderConfirmationAsync(Order order, CancellationToken ct = default);
}

public interface ITaxInvoiceIssuer
{
    Task IssueAsync(Order order, CancellationToken ct = default);
}

public class OrderService
{
    private readonly IOrderRepository _repository;
    private readonly INotificationSender _notifier;
    private readonly ITaxInvoiceIssuer _taxIssuer;

    public OrderService(
        IOrderRepository repository,
        INotificationSender notifier,
        ITaxInvoiceIssuer taxIssuer)
    {
        _repository = repository;
        _notifier = notifier;
        _taxIssuer = taxIssuer;
    }

    public async Task PlaceOrderAsync(Order order, CancellationToken ct = default)
    {
        order.MarkPlaced();
        await _repository.SaveAsync(order, ct);
        await _taxIssuer.IssueAsync(order, ct);
        await _notifier.SendOrderConfirmationAsync(order, ct);
    }
}

// --- Test doubles: the payoff ------------------------------------------------

public class FakeOrderRepository : IOrderRepository
{
    public List<Order> Saved { get; } = new();
    public Task SaveAsync(Order order, CancellationToken ct = default)
    {
        Saved.Add(order);
        return Task.CompletedTask;
    }
}

public class FakeNotificationSender : INotificationSender
{
    public bool WasSent { get; private set; }
    public DateTime? SentAt { get; private set; }

    public Task SendOrderConfirmationAsync(Order order, CancellationToken ct = default)
    {
        WasSent = true;
        SentAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }
}

public class FakeTaxInvoiceIssuer : ITaxInvoiceIssuer
{
    public bool WasIssued { get; private set; }
    public DateTime? IssuedAt { get; private set; }

    public Task IssueAsync(Order order, CancellationToken ct = default)
    {
        WasIssued = true;
        IssuedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }
}
