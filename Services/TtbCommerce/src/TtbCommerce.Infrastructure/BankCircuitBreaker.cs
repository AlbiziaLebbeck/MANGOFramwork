using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

// Process-local, per operation. No timers/probes, retries or automatic bank calls, especially no MerchantVoid replay.
public sealed class BankCircuitBreaker
{
    private sealed class State
    {
        public int Failures;
        public long Generation;
        public DateTimeOffset? OpenUntil;
        public bool TrialInFlight;
    }
    public sealed class Permit
    {
        internal Permit(BankOperation operation, long generation, bool trial) { Operation = operation; Generation = generation; Trial = trial; }
        internal BankOperation Operation { get; }
        internal long Generation { get; }
        internal bool Trial { get; }
        internal bool Completed;
    }
    public sealed record Snapshot(BankOperation Operation, bool Open, int ConsecutiveFailures, double SecondsRemaining);
    private readonly object gate = new();
    private readonly State[] states = [new(), new(), new(), new()];
    private readonly int threshold;
    private readonly TimeSpan duration;
    private readonly TimeProvider clock;

    public BankCircuitBreaker(BankResilienceOptions options, TimeProvider clock)
    {
        if (options.FailureThreshold is < 2 or > 20 || options.BreakSeconds is < 1 or > 300)
            throw new InvalidOperationException("Bank resilience requires a failure threshold of2–20 and a break duration of1–300seconds.");
        threshold = options.FailureThreshold; duration = TimeSpan.FromSeconds(options.BreakSeconds); this.clock = clock;
    }
    private State For(BankOperation operation) => operation switch
    {
        BankOperation.Verify => states[0], BankOperation.EnquiryRange => states[1], BankOperation.MerchantVoid => states[2], BankOperation.EnquiryVoid => states[3],
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };
    public static string ServiceName(BankOperation operation) => operation switch
    {
        BankOperation.Verify => "verify-qr", BankOperation.EnquiryRange => "enquiry-qr-range", BankOperation.MerchantVoid => "merchant-void", BankOperation.EnquiryVoid => "enquiry-void",
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };
    public void AssertAvailable(BankOperation operation)
    {
        lock (gate)
        {
            var state = For(operation);
            if (state.OpenUntil > clock.GetUtcNow() || state.TrialInFlight)
                throw new CommerceException(503, "bank_circuit_open", "The requested bank service is temporarily paused. Use an explicit check later; no request was automatically retried.");
        }
    }
    public Permit? TryEnter(BankOperation operation)
    {
        lock (gate)
        {
            var state = For(operation);
            if (state.OpenUntil is not null)
            {
                if (state.OpenUntil > clock.GetUtcNow() || state.TrialInFlight) return null;
                state.TrialInFlight = true;
                return new(operation, state.Generation, true);
            }
            return new(operation, state.Generation, false);
        }
    }
    public void Complete(Permit permit, bool failure)
    {
        lock (gate)
        {
            if (permit.Completed) return;
            permit.Completed = true;
            var state = For(permit.Operation);
            // A response sent before this opening cannot close a newer failed generation.
            if (permit.Generation != state.Generation) return;
            if (failure)
            {
                state.Failures = Math.Min(threshold, state.Failures + 1);
                if (permit.Trial || state.Failures >= threshold)
                {
                    state.OpenUntil = clock.GetUtcNow().Add(duration); state.TrialInFlight = false; state.Generation++;
                }
            }
            else
            {
                state.Failures = 0;
                if (permit.Trial) { state.OpenUntil = null; state.TrialInFlight = false; state.Generation++; }
            }
        }
    }
    public void Abandon(Permit permit) => Complete(permit, true);
    public IReadOnlyList<Snapshot> Read()
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            return new[] { BankOperation.Verify, BankOperation.EnquiryRange, BankOperation.MerchantVoid, BankOperation.EnquiryVoid }
                .Select(operation => { var state = For(operation); return new Snapshot(operation, state.OpenUntil is not null, state.Failures,
                    state.OpenUntil is null ? 0 : Math.Max(0, (state.OpenUntil.Value - now).TotalSeconds)); }).ToArray();
        }
    }
}
