using LexisNexis.DocumentIntake.BusinessLogic.Domain;
using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using LexisNexis.DocumentIntake.BusinessLogic.Models;
using LexisNexis.DocumentIntake.Infrastructure.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using FluentAssertions;
using Document = LexisNexis.DocumentIntake.BusinessLogic.Domain.Document;

namespace LexisNexis.DocumentIntake.Tests.Workers
{
    public class DocumentProcessingWorkerTests
    {
        [Fact]
        public async Task Worker_WhenProcessingSucceeds_SetsStatusToProcessed()
        {
            // Arrange
            var repo = Substitute.For<IDocumentRepository>();
            var queue = Substitute.For<IQueueService>();
            var storage = Substitute.For<IStorageService>();
            var metrics = Substitute.For<IMetricsService>();
            var deadLetter = new DeadLetterService(Substitute.For<ILogger<DeadLetterService>>());
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Processing:MaxPreviewLength"] = "500",
                    ["Processing:WorkerDelayMs"] = "0"
                }).Build();

            var doc = Document.CreateNew("DOC-001", "Provider", "Title",
                null, [], "text/plain", "test.txt");
            var storageKey = new StorageKey("provider", doc.Id, "test.txt");
            doc.MarkAsStored(storageKey);
            doc.MarkAsQueued();

            var message = new ProcessingMessage
            {
                DocumentId = doc.Id,
                SubmittedAt = DateTimeOffset.UtcNow
            };

            queue.DequeueAsync(Arg.Any<CancellationToken>())
                 .Returns(message, (ProcessingMessage?)null);

            repo.FindByIdAsync(doc.Id, Arg.Any<CancellationToken>())
                .Returns(doc);

            storage.DownloadAsync(storageKey, Arg.Any<CancellationToken>())
                   .Returns(new MemoryStream("Legal document content here."u8.ToArray()));

            // Act
            var cts = new CancellationTokenSource();
            var worker = new DocumentProcessingWorker(
                queue, repo, storage, metrics, deadLetter,
                Substitute.For<ILogger<DocumentProcessingWorker>>(), config);

            cts.CancelAfter(TimeSpan.FromSeconds(2));
            await worker.StartAsync(cts.Token);
            await Task.Delay(500);
            await worker.StopAsync(CancellationToken.None);

            // Assert
            doc.Status.Should().Be(ProcessingStatus.Processed);
            doc.Preview.Should().NotBeNullOrEmpty();
            await metrics.Received(1)
                .IncrementAsync("DocumentProcessed", Arg.Any<CancellationToken>());
        }
    }
}
