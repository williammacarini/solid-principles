namespace SolidPrinciples.LSP;

// ---------------------------------------------------------------------------
// BEFORE — a subtype that cannot honor the base contract.
//
// `LspPix` is a `RefundablePayment` to the compiler, but not in behavior.
// Callers written against the base type break at runtime.
// ---------------------------------------------------------------------------

public class RefundablePaymentBefore
{
    public string Id { get; set; } = "";
    public virtual Task RefundAsync(decimal amount, CancellationToken ct = default)
        => Task.CompletedTask;
}

public class LspPixBefore : RefundablePaymentBefore
{
    public override Task RefundAsync(decimal amount, CancellationToken ct = default)
        => throw new NotSupportedException("Pix does not support refunds.");
}

public class ReconciliationBefore
{
    /// <summary>Looks safe. Throws for every Pix payment.</summary>
    public async Task ReconcileAsync(IEnumerable<RefundablePaymentBefore> payments, decimal amount)
    {
        foreach (var payment in payments)
            await payment.RefundAsync(amount);
    }
}

// ---------------------------------------------------------------------------
// AFTER — split by capability, so nothing is promised that cannot be delivered.
// ---------------------------------------------------------------------------

public interface IPayment
{
    string Id { get; }
    decimal Amount { get; }
}

public interface IRefundablePayment
{
    Task RefundAsync(decimal amount, CancellationToken ct = default);
}

public class LspCreditCard : IPayment, IRefundablePayment
{
    public string Id { get; init; } = "";
    public decimal Amount { get; init; }

    public Task RefundAsync(decimal amount, CancellationToken ct = default)
        => Task.CompletedTask;
}

/// <summary>A payment that is simply not refundable. No lying, no throwing.</summary>
public class LspPix : IPayment
{
    public string Id { get; init; } = "";
    public decimal Amount { get; init; }
}

public class Reconciliation
{
    /// <summary>Only refundable things can reach this method. Enforced by the compiler.</summary>
    public async Task ReconcileAsync(IEnumerable<IRefundablePayment> payments, decimal amount)
    {
        foreach (var payment in payments)
            await payment.RefundAsync(amount);
    }
}
