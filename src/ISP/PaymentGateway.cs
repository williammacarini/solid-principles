namespace SolidPrinciples.ISP;

// ---------------------------------------------------------------------------
// BEFORE — a fat interface describing the union of every gateway's abilities.
//
// Each implementer supports a fraction and throws for the rest. Callers cannot
// tell from the signature whether a call is safe.
// ---------------------------------------------------------------------------

public record Payment(string Amount, string Method);
public record PaymentResult(bool Success, string TransactionId);
public record Subscription(string PlanId);
public record Customer(string Email);

public interface IPaymentGatewayBefore
{
    Task<PaymentResult> ChargeAsync(Payment payment);
    Task RefundAsync(string transactionId, decimal amount);
    Task<string> CreateSubscriptionAsync(Subscription subscription);
    Task CancelSubscriptionAsync(string subscriptionId);
    Task<Customer> CreateCustomerAsync(Customer customer);
}

public class IspPixGatewayBefore : IPaymentGatewayBefore
{
    public Task<PaymentResult> ChargeAsync(Payment payment)
        => Task.FromResult(new PaymentResult(true, "pix_1"));

    // 4 of 5 members are landmines.
    public Task RefundAsync(string transactionId, decimal amount) => throw new NotSupportedException();
    public Task<string> CreateSubscriptionAsync(Subscription subscription) => throw new NotSupportedException();
    public Task CancelSubscriptionAsync(string subscriptionId) => throw new NotSupportedException();
    public Task<Customer> CreateCustomerAsync(Customer customer) => throw new NotSupportedException();
}

// ---------------------------------------------------------------------------
// AFTER — segregated by capability. Every member of every interface is real.
// ---------------------------------------------------------------------------

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

/// <summary>Declares exactly what it supports.</summary>
public class IspPixGateway : IChargesPayments, IRefundsPayments
{
    public Task<PaymentResult> ChargeAsync(Payment payment, CancellationToken ct = default)
        => Task.FromResult(new PaymentResult(true, "pix_1"));

    public Task RefundAsync(string transactionId, decimal amount, CancellationToken ct = default)
        => Task.CompletedTask;
}

public class IspCreditCardGateway : IChargesPayments, IRefundsPayments, IManagesSubscriptions
{
    public Task<PaymentResult> ChargeAsync(Payment payment, CancellationToken ct = default)
        => Task.FromResult(new PaymentResult(true, "cc_1"));

    public Task RefundAsync(string transactionId, decimal amount, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<string> CreateSubscriptionAsync(Subscription subscription, CancellationToken ct = default)
        => Task.FromResult($"sub_{Guid.NewGuid():N}");

    public Task CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
        => Task.CompletedTask;
}

/// <summary>Depends only on what it uses — and every implementation genuinely charges.</summary>
public class CheckoutService
{
    private readonly IChargesPayments _gateway;

    public CheckoutService(IChargesPayments gateway) => _gateway = gateway;

    public Task<PaymentResult> CheckoutAsync(Payment payment, CancellationToken ct = default)
        => _gateway.ChargeAsync(payment, ct);
}
