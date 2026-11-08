namespace SolidPrinciples.OCP;

// ---------------------------------------------------------------------------
// BEFORE — every new payment method edits working, tested code.
//
// The switch grows without bound, nothing tells the compiler a case is
// missing, and `default` turns that omission into a runtime failure.
// ---------------------------------------------------------------------------

public record Payment(string Method, decimal Amount);
public record PaymentResult(bool Success, string TransactionId);

public class PaymentProcessorBefore
{
    public Task<PaymentResult> ProcessAsync(Payment payment) => payment.Method switch
    {
        "credit_card" => ProcessCreditCard(payment),
        "pix" => ProcessPix(payment),
        "boleto" => ProcessBoleto(payment),
        _ => throw new NotSupportedException($"Unknown method: {payment.Method}")
    };

    private Task<PaymentResult> ProcessCreditCard(Payment p) => Task.FromResult(new PaymentResult(true, "cc_1"));
    private Task<PaymentResult> ProcessPix(Payment p) => Task.FromResult(new PaymentResult(true, "pix_1"));
    private Task<PaymentResult> ProcessBoleto(Payment p) => Task.FromResult(new PaymentResult(true, "bol_1"));
}

// ---------------------------------------------------------------------------
// AFTER — a new method is a new class. The dispatcher never changes.
// ---------------------------------------------------------------------------

public interface IPaymentMethod
{
    string Key { get; }
    Task<PaymentResult> ProcessAsync(Payment payment, CancellationToken ct = default);
}

public class OcpCreditCardPayment : IPaymentMethod
{
    public string Key => "credit_card";
    public Task<PaymentResult> ProcessAsync(Payment payment, CancellationToken ct = default)
        => Task.FromResult(new PaymentResult(true, $"cc_{Guid.NewGuid():N}"));
}

public class OcpPixPayment : IPaymentMethod
{
    public string Key => "pix";
    public Task<PaymentResult> ProcessAsync(Payment payment, CancellationToken ct = default)
        => Task.FromResult(new PaymentResult(true, $"pix_{Guid.NewGuid():N}"));
}

public class OcpBoletoPayment : IPaymentMethod
{
    public string Key => "boleto";
    public Task<PaymentResult> ProcessAsync(Payment payment, CancellationToken ct = default)
        => Task.FromResult(new PaymentResult(true, $"bol_{Guid.NewGuid():N}"));
}

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
