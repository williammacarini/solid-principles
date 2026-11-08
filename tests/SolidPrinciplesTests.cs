using SolidPrinciples.DIP;
using SolidPrinciples.ISP;
using SolidPrinciples.LSP;
using SolidPrinciples.OCP;
using SolidPrinciples.SRP;
using Xunit;

// OCP and ISP both model a Payment/PaymentResult pair. Aliasing keeps the
// intent explicit at each call site instead of relying on using order.
using OcpPayment = SolidPrinciples.OCP.Payment;
using OcpResult = SolidPrinciples.OCP.PaymentResult;
using IspPayment = SolidPrinciples.ISP.Payment;

namespace SolidPrinciples.Tests;

// ---------------------------------------------------------------------------
// These tests exist to demonstrate the *payoff* of each refactor — not to
// reach a coverage number. Note how trivially the "after" versions test.
// ---------------------------------------------------------------------------

public class SrpTests
{
    [Fact]
    public void Calculates_18_percent_tax_on_subtotal()
    {
        var invoice = new Invoice();
        invoice.Items.Add(new InvoiceItem("Consulting", Price: 100m, Quantity: 3));

        var totals = new InvoiceCalculator().Calculate(invoice);

        Assert.Equal(300m, totals.Subtotal);
        Assert.Equal(54m, totals.Tax);
        Assert.Equal(354m, totals.Total);
    }

    [Fact]
    public async Task Coordinates_all_three_collaborators()
    {
        var invoice = new Invoice();
        invoice.Items.Add(new InvoiceItem("Cake", Price: 50m, Quantity: 2));

        var repository = new SpyRepository();
        var notifier = new SpyNotifier();
        var service = new InvoiceService(new InvoiceCalculator(), repository, notifier);

        await service.ProcessAsync(invoice);

        Assert.Equal(100m, invoice.Subtotal);
        Assert.True(repository.WasCalled);
        Assert.True(notifier.WasCalled);
    }

    private class SpyRepository : IInvoiceRepository
    {
        public bool WasCalled { get; private set; }
        public Task SaveAsync(Invoice invoice, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.CompletedTask;
        }
    }

    private class SpyNotifier : IInvoiceNotifier
    {
        public bool WasCalled { get; private set; }
        public Task SendAsync(Invoice invoice, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.CompletedTask;
        }
    }
}

public class OcpTests
{
    [Fact]
    public async Task Dispatches_to_the_registered_method()
    {
        var processor = new PaymentProcessor(new IPaymentMethod[]
        {
            new OcpCreditCardPayment(),
            new OcpPixPayment(),
            new OcpBoletoPayment(),
        });

        var result = await processor.ProcessAsync(new OcpPayment("pix", 100m));

        Assert.True(result.Success);
        Assert.StartsWith("pix_", result.TransactionId);
    }

    [Fact]
    public async Task Supports_a_new_method_without_touching_the_dispatcher()
    {
        // A brand-new payment method — added as a class, dispatcher untouched.
        var processor = new PaymentProcessor(new IPaymentMethod[]
        {
            new OcpCreditCardPayment(),
            new FakeWalletPayment(),
        });

        var result = await processor.ProcessAsync(new OcpPayment("wallet", 25m));

        Assert.True(result.Success);
        Assert.Equal("wallet_1", result.TransactionId);
    }

    [Fact]
    public async Task Rejects_an_unknown_method()
    {
        var processor = new PaymentProcessor(Array.Empty<IPaymentMethod>());

        await Assert.ThrowsAsync<NotSupportedException>(
            () => processor.ProcessAsync(new OcpPayment("crypto", 10m)));
    }

    private class FakeWalletPayment : IPaymentMethod
    {
        public string Key => "wallet";
        public Task<OcpResult> ProcessAsync(OcpPayment payment, CancellationToken ct = default)
            => Task.FromResult(new OcpResult(true, "wallet_1"));
    }
}

public class LspTests
{
    [Fact]
    public async Task Refunds_every_refundable_payment()
    {
        var reconciliation = new Reconciliation();
        var payments = new List<IRefundablePayment>
        {
            new LspCreditCard { Id = "cc_1", Amount = 100m },
            new LspCreditCard { Id = "cc_2", Amount = 250m },
        };

        // No exception: every element genuinely honors the contract.
        await reconciliation.ReconcileAsync(payments, 10m);
    }

    [Fact]
    public void Pix_is_a_payment_but_not_refundable()
    {
        IPayment pix = new LspPix { Id = "pix_1", Amount = 75m };

        Assert.Equal(75m, pix.Amount);
        Assert.False(pix is IRefundablePayment);  // the type system tells the truth
    }
}

public class IspTests
{
    [Fact]
    public async Task Pix_gateway_charges_without_unimplemented_members()
    {
        IChargesPayments gateway = new IspPixGateway();

        var result = await gateway.ChargeAsync(new IspPayment("100", "pix"));

        Assert.True(result.Success);
        // IChargesPayments has exactly one member — nothing can throw here.
    }

    [Fact]
    public async Task Credit_card_gateway_also_manages_subscriptions()
    {
        IManagesSubscriptions gateway = new IspCreditCardGateway();

        var subscriptionId = await gateway.CreateSubscriptionAsync(new Subscription("pro"));

        Assert.StartsWith("sub_", subscriptionId);
    }
}

public class DipTests
{
    [Fact]
    public async Task Places_order_and_issues_invoice_before_notifying()
    {
        var repository = new FakeOrderRepository();
        var notifier = new FakeNotificationSender();
        var taxIssuer = new FakeTaxInvoiceIssuer();
        var service = new OrderService(repository, notifier, taxIssuer);

        var order = new Order { Id = 1, CustomerEmail = "buyer@example.com" };
        await service.PlaceOrderAsync(order);

        Assert.Equal("placed", order.Status);
        Assert.Single(repository.Saved);

        // The actual business rule: the invoice must exist before the customer
        // is told the order succeeded.
        Assert.True(taxIssuer.WasIssued);
        Assert.True(notifier.WasSent);
        Assert.True(taxIssuer.IssuedAt <= notifier.SentAt);
    }
}
