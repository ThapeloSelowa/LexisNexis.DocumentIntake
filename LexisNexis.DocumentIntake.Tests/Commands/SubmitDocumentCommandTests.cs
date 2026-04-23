using LexisNexis.DocumentIntake.BusinessLogic.Commands;
using LexisNexis.DocumentIntake.BusinessLogic.Domain;
using LexisNexis.DocumentIntake.BusinessLogic.Interfaces;
using LexisNexis.DocumentIntake.BusinessLogic.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using FluentAssertions;

using Document = LexisNexis.DocumentIntake.BusinessLogic.Domain.Document;

namespace LexisNexis.DocumentIntake.Tests.Commands
{
    /// <summary>
    /// Tests the core deduplication behaviour.
    /// Uses NSubstitute to mock infrastructure
    /// </summary>
    public class SubmitDocumentCommandTests
    {
        private readonly IDocumentRepository _repo = Substitute.For<IDocumentRepository>();
        private readonly IStorageService _storage = Substitute.For<IStorageService>();
        private readonly IQueueService _queue = Substitute.For<IQueueService>();
        private readonly IMetricsService _metrics = Substitute.For<IMetricsService>();
        private readonly ILogger<SubmitDocumentCommandHandler> _logger = Substitute.For<ILogger<SubmitDocumentCommandHandler>>();

        private readonly SubmitDocumentCommandHandler _handler;

        public SubmitDocumentCommandTests()
        {
            _handler = new SubmitDocumentCommandHandler( _repo, _storage, _queue, _metrics, _logger);

            // Default: storage always returns a valid key
            _storage.UploadAsync(Arg.Any<DocumentId>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
                .Returns(new StorageKey("test-provider", DocumentId.New(), "test.pdf"));
        }

        [Fact]
        public async Task Handle_NewDocument_CreatesNewRecord_AndQueuesProcessing()
        {
            // Arrange — no existing document
            _repo.FindByDedupKeyAsync(Arg.Any<DedupKey>(), Arg.Any<CancellationToken>())
                 .Returns((Document?)null);

            var command = BuildCommand();

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.IsResubmission.Should().BeFalse();
            result.DocumentId.Value.Should().NotBeEmpty();

            await _storage.Received(1)
                .UploadAsync(Arg.Any<DocumentId>(), Arg.Any<string>(),
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());

            await _queue.Received(1)
                .EnqueueAsync(Arg.Is<ProcessingMessage>(m =>
                    m.DocumentId == result.DocumentId), Arg.Any<CancellationToken>());

            await _repo.Received()
                .UpsertAsync(Arg.Any<Document>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task Handle_ResubmittedDocument_UpdatesExistingRecord_NotCreateNew()
        {
            // Arrange — existing document in repo
            var existing = Document.CreateNew("DOC-001", "TestProvider",
                "Test Doc", null, [], "text/plain", "test.txt");

            _repo.FindByDedupKeyAsync(Arg.Any<DedupKey>(), Arg.Any<CancellationToken>())
                 .Returns(existing);

            var command = BuildCommand();
            var originalId = existing.Id;

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert — same document, not a new one
            result.IsResubmission.Should().BeTrue();
            result.DocumentId.Should().Be(originalId);

            await _metrics.Received(1)
                .IncrementAsync("DocumentResubmitted", Arg.Any<CancellationToken>());
        }

        private static SubmitDocumentCommandRequest BuildCommand() => new()
        {
            SourceDocumentId = "DOC-001",
            Provider = "TestProvider",
            Title = "Test Document",
            ContentType = "text/plain",
            FileName = "test.txt",
            FileContent = new MemoryStream("Hello world"u8.ToArray())
        };
    }
}
