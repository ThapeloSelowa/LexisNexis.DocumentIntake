using Amazon.SQS;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using LexisNexis.DocumentIntake.BusinessLogic.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using System.Text.Json;


namespace LexisNexis.DocumentIntake.Infrastructure.Queue
{
    /// <summary>
    /// Production SQS queue implementation.
    /// Uses the same IQueueService contract as InMemoryQueueService —
    /// swap between them via DI configuration only.
    /// </summary>
    public class SqsQueueService(IAmazonSQS sqs,IConfiguration config,ILogger<SqsQueueService> logger) : IQueueService
    {
        private readonly string _queueUrl = config["AWS:QueueUrl"]
            ?? throw new InvalidOperationException("AWS:QueueUrl is not configured.");

        public async Task EnqueueAsync(ProcessingMessage message, CancellationToken ct = default)
        {
            var body = JsonSerializer.Serialize(message);

            await sqs.SendMessageAsync(new SendMessageRequest
            {
                QueueUrl = _queueUrl,
                MessageBody = body,
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["DocumentId"] = new()
                    {
                        DataType = "String",
                        StringValue = message.DocumentId.ToString()
                    }
                }
            }, ct);

            logger.LogInformation("Enqueued message for DocumentId {DocumentId}", message.DocumentId);
        }

        public async Task<ProcessingMessage?> DequeueAsync(CancellationToken ct = default)
        {
            var response = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = _queueUrl,
                MaxNumberOfMessages = 1,
                WaitTimeSeconds = 20  // Long polling — efficient
            }, ct);

            var msg = response.Messages.FirstOrDefault();
            if (msg is null) return null;

            // Delete from SQS immediately (at-least-once delivery)
            await sqs.DeleteMessageAsync(_queueUrl, msg.ReceiptHandle, ct);

            return JsonSerializer.Deserialize<ProcessingMessage>(msg.Body);
        }
    }
}
