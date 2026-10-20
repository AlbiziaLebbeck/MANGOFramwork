using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TtbCommerce.Core;

namespace TtbCommerce.Infrastructure;

public interface ICommerceJobHandler
{
    string Kind { get; }
    Task HandleAsync(string payloadJson, CancellationToken cancellationToken);
}

public sealed class OrderExpiryHandler(OrderService orders) : ICommerceJobHandler
{
    public string Kind => "order.expire";
    public async Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(payloadJson);
        await orders.ExpireAsync(document.RootElement.GetProperty("orderId").GetGuid(), cancellationToken);
    }
}

public sealed class CommerceWorker(IServiceScopeFactory scopes, CommerceOptions options, TimeProvider clock, ILogger<CommerceWorker> logger, OperationsOptions? operations = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (operations?.RecoveryFreeze == true || options.Role is not RuntimeRole.All and not RuntimeRole.CommerceWorker) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var handlers = scope.ServiceProvider.GetServices<ICommerceJobHandler>().ToDictionary(x => x.Kind, StringComparer.Ordinal);
                var work = scope.ServiceProvider.GetRequiredService<DurableWork>();
                var lease = await work.ClaimAsync(handlers.Keys.ToArray(), TimeSpan.FromMinutes(1), stoppingToken);
                if (lease is null) { await Task.Delay(TimeSpan.FromSeconds(1), clock, stoppingToken); continue; }
                try
                {
                    await handlers[lease.Kind].HandleAsync(lease.PayloadJson, stoppingToken);
                    if (!await work.CompleteAsync(lease, stoppingToken)) logger.LogWarning("Job lease expired before completion: {JobId}", lease.Id);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception exception)
                {
                    var safeError = exception is CommerceException commerce ? commerce.Code : exception.GetType().Name;
                    logger.LogWarning("Job {JobId} ({Kind}) failed with {SafeCode}, attempt {Attempt}", lease.Id, lease.Kind, safeError, lease.Attempts);
                    await work.RetryAsync(lease, safeError, TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(lease.Attempts, 6)))), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogWarning("Commerce worker unavailable: {SafeType}", exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(5), clock, stoppingToken);
            }
        }
    }
}
