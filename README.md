# SOLID Principles in C#

Five principles, each shown twice: **the violation** and **the fix**. The goal is not to quote the definitions — it's to show the specific pain each principle removes.

> Every example is a complete, compilable C# file. Each folder has a `before/` and an `after/` version, and the reasoning behind the change.

---

## Why this repository exists

SOLID is the most quoted and least applied set of principles in software. The definitions are easy to recite:

- **S**ingle Responsibility — a class should have one reason to change
- **O**pen/Closed — open for extension, closed for modification
- **L**iskov Substitution — subtypes must be usable in place of their base type
- **I**nterface Segregation — no client should depend on methods it doesn't use
- **D**ependency Inversion — depend on abstractions, not concretions

Reciting them is worthless. Recognizing the *smell* that means you're violating one, and knowing what the fix costs — that's the skill. So each example below starts from code that looks reasonable, names the concrete failure it produces, and shows the refactor.

Most SOLID tutorials use shapes and animals. These use the domain I actually work in: **payments, billing, and order processing**. That matters, because the trade-offs are real — the "after" version is not always shorter, and a couple of these refactors are only worth doing once the second case appears.

---

## The examples

| Principle | Folder | The concrete failure it fixes |
|---|---|---|
| **SRP** | [`src/SRP/`](src/SRP/) | A class that changes for three unrelated reasons, so a pricing change breaks the email formatting |
| **OCP** | [`src/OCP/`](src/OCP/) | Every new payment method requires editing a growing `switch` in the middle of working code |
| **LSP** | [`src/LSP/`](src/LSP/) | A subtype that throws on a method it inherited, breaking callers that trusted the base contract |
| **ISP** | [`src/ISP/`](src/ISP/) | An implementer forced to write three `throw new NotImplementedException()` methods |
| **DIP** | [`src/DIP/`](src/DIP/) | Business logic that can't be tested without a live database and an SMTP server |

---

## 1. Single Responsibility Principle

> A class should have only one reason to change.

### The violation

```csharp
public class InvoiceService
{
    public void ProcessInvoice(Invoice invoice)
    {
        // 1. Business rule: calculate totals and tax
        invoice.Total = invoice.Items.Sum(i => i.Price * i.Quantity);
        invoice.Tax = invoice.Total * 0.18m;

        // 2. Persistence: write to the database
        using var conn = new SqlConnection(_connectionString);
        conn.Execute("INSERT INTO invoices ...", invoice);

        // 3. Presentation: format and send an email
        var body = $"<h1>Invoice {invoice.Number}</h1><p>Total: {invoice.Total:C}</p>";
        var smtp = new SmtpClient("smtp.example.com");
        smtp.Send("billing@example.com", invoice.CustomerEmail, "Your invoice", body);
    }
}
```

This compiles, passes a smoke test, and looks fine in a small codebase.

### Why it's a problem

Three unrelated reasons to change are fused into one class:

1. **Tax rules change** — the finance team updates the rate.
2. **The schema changes** — a new column is added.
3. **The email template changes** — marketing wants a new layout.

Each of these forces a modification to `InvoiceService`. And because the email HTML is built inline, **a change to the tax calculation means re-testing the email**, and vice versa. Worse: you cannot unit test the tax math without a database connection and an SMTP server, so in practice the tax math doesn't get tested at all.

That last consequence is the real cost. Not "the class is long" — **the business rule that moves money is untested because it's welded to I/O.**

### The fix

Separate the three responsibilities behind their own types:

```csharp
// Business rule — pure, no I/O, fast to test
public class InvoiceCalculator
{
    private const decimal TaxRate = 0.18m;

    public InvoiceTotals Calculate(Invoice invoice)
    {
        var subtotal = invoice.Items.Sum(i => i.Price * i.Quantity);
        return new InvoiceTotals(subtotal, subtotal * TaxRate);
    }
}

// Persistence
public interface IInvoiceRepository
{
    Task SaveAsync(Invoice invoice, CancellationToken ct = default);
}

// Presentation
public interface IInvoiceNotifier
{
    Task SendAsync(Invoice invoice, CancellationToken ct = default);
}

// Orchestration only — coordinates, decides nothing
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
```

Now the tax rule is a pure function that tests in microseconds:

```csharp
[Fact]
public void Calculates_18_percent_tax_on_subtotal()
{
    var invoice = new Invoice().WithItem("Consulting", price: 100m, quantity: 3);

    var totals = new InvoiceCalculator().Calculate(invoice);

    Assert.Equal(300m, totals.Subtotal);
    Assert.Equal(54m, totals.Tax);
}
```

**The trade-off, honestly:** there are now four types instead of one, and `InvoiceService` is a thin coordinator. For a genuinely trivial case this is over-engineering. The signal that it's warranted is the one above — **you can't test the important rule without standing up infrastructure.**

→ [`src/SRP/`](src/SRP/)

---

## 2. Open/Closed Principle

> Software entities should be open for extension but closed for modification.

### The violation

```csharp
public class PaymentProcessor
{
    public async Task<PaymentResult> ProcessAsync(Payment payment)
    {
        switch (payment.Method)
        {
            case "credit_card":
                return await ProcessCreditCard(payment);

            case "pix":
                return await ProcessPix(payment);

            case "boleto":
                return await ProcessBoleto(payment);

            default:
                throw new NotSupportedException($"Unknown method: {payment.Method}");
        }
    }
}
```

### Why it's a problem

Adding a payment method means **editing this method** — code that is already working, already tested, and currently in production. Every addition is a chance to break an existing method, and the `switch` grows without bound. The compiler offers no help: nothing tells you that you forgot a case, and `default` throws at runtime instead.

You can see the pressure in the code itself — every new method adds a `case`, and the method does more every quarter.

### The fix

Give each method its own type behind a shared abstraction:

```csharp
public interface IPaymentMethod
{
    string Key { get; }
    Task<PaymentResult> ProcessAsync(Payment payment, CancellationToken ct = default);
}

public class CreditCardPayment : IPaymentMethod
{
    public string Key => "credit_card";
    public Task<PaymentResult> ProcessAsync(Payment payment, CancellationToken ct = default) { ... }
}

public class PixPayment : IPaymentMethod
{
    public string Key => "pix";
    public Task<PaymentResult> ProcessAsync(Payment payment, CancellationToken ct = default) { ... }
}

// Dispatcher — never changes again
public class PaymentProcessor
{
    private readonly IReadOnlyDictionary<string, IPaymentMethod> _methods;

    public PaymentProcessor(IEnumerable<IPaymentMethod> methods)
        => _methods = methods.ToDictionary(m => m.Key);

    public Task<PaymentResult> ProcessAsync(Payment payment, CancellationToken ct = default)
        => _methods.TryGetValue(payment.Method, out var method)
            ? method.ProcessAsync(payment, ct)
            : throw new NotSupportedException($"Unknown method: {payment.Method}");
}
```

A new payment method is a **new class** — `PaymentProcessor` is untouched, and its tests keep passing.

**The trade-off, honestly:** this is only worth it once you have **three or more** cases, or you know more are coming. With two stable branches, a `switch` is clearer and the abstraction is noise. The refactor's value is proportional to how often the set grows.

→ [`src/OCP/`](src/OCP/)

---

## 3. Liskov Substitution Principle

> Subtypes must be substitutable for their base types without altering correctness.

### The violation

```csharp
public class RefundablePayment
{
    public virtual void Refund(decimal amount) { /* performs refund */ }
}

public class PixPayment : RefundablePayment
{
    public override void Refund(decimal amount)
        => throw new NotSupportedException("Pix does not support refunds.");
}
```

### Why it's a problem

`PixPayment` *is a* `RefundablePayment` in the type system, but not in behavior. Any code written against the base class breaks when handed a `PixPayment`:

```csharp
public void ReconcileDaily(IEnumerable<RefundablePayment> payments, decimal amount)
{
    foreach (var payment in payments)
        payment.Refund(amount);   // 💥 throws for every Pix payment
}
```

The compiler approves this code. The type hierarchy lied.

This is the **most commonly violated** principle, because it's the one where inheritance feels natural: "Pix is a payment, so let it extend Payment."

### The fix

Don't model a capability that the subtype doesn't have. Split the hierarchy by capability:

```csharp
public interface IPayment { /* common: amount, status, id */ }

public interface IRefundable
{
    Task RefundAsync(decimal amount, CancellationToken ct = default);
}

public class CreditCardPayment : IPayment, IRefundable
{
    public Task RefundAsync(decimal amount, CancellationToken ct = default) { ... }
}

// Pix is a payment — but simply not refundable. No lying.
public class PixPayment : IPayment { }
```

Now the compiler enforces the contract:

```csharp
public async Task ReconcileDaily(IEnumerable<IRefundable> refundables, decimal amount)
{
    foreach (var refundable in refundables)
        await refundable.RefundAsync(amount);   // ✅ only refundable things get here
}
```

**The test that catches it:** if any override throws `NotSupportedException` or is an empty no-op, the hierarchy is probably wrong.

→ [`src/LSP/`](src/LSP/)

---

## 4. Interface Segregation Principle

> No client should be forced to depend on methods it does not use.

### The violation

```csharp
public interface IPaymentGateway
{
    Task<PaymentResult> ChargeAsync(Payment payment);
    Task RefundAsync(string transactionId, decimal amount);
    Task<string> CreateSubscriptionAsync(Subscription subscription);
    Task CancelSubscriptionAsync(string subscriptionId);
    Task<Customer> CreateCustomerAsync(Customer customer);
}

public class PixGateway : IPaymentGateway
{
    public Task<PaymentResult> ChargeAsync(Payment payment) { /* real */ }

    public Task RefundAsync(string transactionId, decimal amount)
        => throw new NotSupportedException();

    public Task<string> CreateSubscriptionAsync(Subscription subscription)
        => throw new NotSupportedException();

    public Task CancelSubscriptionAsync(string subscriptionId)
        => throw new NotSupportedException();

    public Task<Customer> CreateCustomerAsync(Customer customer)
        => throw new NotSupportedException();
}
```

### Why it's a problem

Four of five methods are landmines. The interface describes the union of every gateway's capabilities, so each implementer supports a fraction and throws for the rest.

The damage propagates to callers: code written against `IPaymentGateway` **looks** like it can create a subscription, and fails at runtime depending on which implementation was injected. You cannot tell from the signature whether it's safe.

### The fix

Segregate by capability, so each interface describes an actual set of supported operations:

```csharp
public interface IChargesPayments
{
    Task<PaymentResult> ChargeAsync(Payment payment, CancellationToken ct = default);
}

public interface IRefundsPayments
{
    Task RefundAsync(string transactionId, decimal amount, CancellationToken ct = default);
}

public interface IManagesSubscriptions
{
    Task<string> CreateSubscriptionAsync(Subscription subscription, CancellationToken ct = default);
    Task CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default);
}

// Declares exactly what it supports — nothing it doesn't.
public class PixGateway : IChargesPayments, IRefundsPayments { ... }
public class CreditCardGateway : IChargesPayments, IRefundsPayments, IManagesSubscriptions { ... }
```

A consumer that charges payments depends only on `IChargesPayments` — and **every** implementation of that interface genuinely charges.

A useful signal: a wide interface where implementations throw for most members. That's not a coding problem, it's a modeling one.

→ [`src/ISP/`](src/ISP/)

---

## 5. Dependency Inversion Principle

> High-level modules should not depend on low-level modules; both should depend on abstractions.

### The violation

```csharp
public class OrderService
{
    public async Task PlaceOrderAsync(Order order)
    {
        // Directly coupled to concrete infrastructure
        using var db = new SqlConnection("Server=prod;Database=orders;...");
        await db.ExecuteAsync("INSERT INTO orders ...", order);

        var smtp = new SmtpClient("smtp.prod.example.com");
        await smtp.SendMailAsync("orders@shop.com", order.CustomerEmail, "Order placed", "...");

        var client = new HttpClient();
        await client.PostAsync("https://api.tax-authority.gov/invoice", ...);
    }
}
```

### Why it's a problem

`OrderService` — the class holding the business rule — depends on three concrete external systems, each with a hardcoded endpoint. The consequences compound:

- **Untestable.** A unit test would hit a production database, an SMTP server, and a government API. So `PlaceOrderAsync` is never actually tested; it's only exercised in staging, by hand.
- **Inflexible.** Pointing at a test environment means editing source code.
- **Fragile.** The business logic is entangled with connection strings.

### The fix

Invert the dependencies: the domain declares what it needs, and infrastructure implements it.

```csharp
// The domain defines the contracts it needs — note where these live.
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

// Business logic depends only on abstractions
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
```

Production wires the real implementations; tests wire fakes:

```csharp
[Fact]
public async Task Issues_tax_invoice_before_notifying_customer()
{
    var repository = new FakeOrderRepository();
    var notifier = new FakeNotificationSender();
    var taxIssuer = new FakeTaxInvoiceIssuer();

    var service = new OrderService(repository, notifier, taxIssuer);

    await service.PlaceOrderAsync(new Order { Id = 1 });

    Assert.True(taxIssuer.WasIssued);
    Assert.True(notifier.WasSent);
    Assert.True(taxIssuer.IssuedAt < notifier.SentAt);  // ordering is the actual rule
}
```

**The insight worth internalizing:** the interface belongs to the **consumer**, not the implementer. `IOrderRepository` lives in the domain layer, expressed in domain terms (`SaveAsync(Order)`), not in SQL terms. If the persistence layer dictated the interface, the dependency wouldn't actually be inverted.

**The trade-off, honestly:** more types, and wiring moves to a composition root or DI container. For a script that runs once, this is pure ceremony. It earns its cost when the business rule is important enough that you need to test it — which, for anything handling money, is always.

→ [`src/DIP/`](src/DIP/)

---

## How these principles relate

They are not five independent rules — they reinforce each other:

```
   SRP  ── classes should have one reason to change
    │
    ├──► OCP  ── if responsibilities are separated, new behavior extends
    │            rather than modifies
    │
    ├──► ISP  ── small focused interfaces follow from separated responsibilities
    │
    └──► DIP  ── depending on abstractions requires the abstraction to exist,
                 which is a consequence of separating concerns
              │
              └──► LSP  ── the abstraction is only safe if every implementation
                           honors it
```

In practice: **SRP and DIP deliver most of the value** in typical business code. OCP matters where the set of cases grows. ISP and LSP matter most in library and SDK design, where you don't control the callers.

---

## Running the code

```bash
dotnet build
dotnet test
```

Requires .NET 8 SDK or later.

---

## About

**William Macarini** — Senior Software Engineer, Go & .NET, distributed systems and payments at scale.

- 🌐 [williammacarini.com.br](https://www.williammacarini.com.br)
- 💼 [LinkedIn](https://www.linkedin.com/in/william-macarini-288aa5189)
- 🐙 [github.com/williammacarini](https://github.com/williammacarini)

Examples are drawn from patterns in production billing and payment systems. Feedback and corrections welcome — open an issue.
