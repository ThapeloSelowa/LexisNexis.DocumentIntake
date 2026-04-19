using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using LexisNexis.DocumentIntake.BusinessLogic.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Channels;

namespace LexisNexis.DocumentIntake.Infrastructure.Queue
{
    /// <summary>
    /// Local queue using System.Threading.Channels.
    /// This simulates SQS behaviour without any external dependency.
    /// </summary>
    public class InMemoryQueueService : IQueueService, IDisposable
    {
        // BoundedChannel limits to 1000 items — prevents unbounded memory growth
        private readonly Channel<ProcessingMessage> _channel =
            Channel.CreateBounded<ProcessingMessage>(new BoundedChannelOptions(1000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

        public async Task EnqueueAsync(ProcessingMessage message, CancellationToken ct = default) =>
            await _channel.Writer.WriteAsync(message, ct);

        public async Task<ProcessingMessage?> DequeueAsync(CancellationToken ct = default)
        {
            try
            {
                return await _channel.Reader.ReadAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public void Dispose() => _channel.Writer.TryComplete();
    }
}
