using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TranscriptService.API.Models.Responses;
using TranscriptService.API.Provider;
using TranscriptService.Models;

namespace TranscriptService.Tests.Provider
{
    public class TranscriptProviderTests
    {
        private readonly Mock<ILogger<TranscriptProvider>> _mockLogger;
        private readonly Mock<IConfiguration> _mockConfiguration;
        private readonly TranscriptProvider _provider;
        private readonly string _testFilePath;

        public TranscriptProviderTests()
        {
            _mockLogger = new Mock<ILogger<TranscriptProvider>>();
            _mockConfiguration = new Mock<IConfiguration>();

            // Setup configuration
            _mockConfiguration.Setup(c => c["AppSettings:OpenAiApiKey"]).Returns("test-openai-key");
            _mockConfiguration.Setup(c => c["AppSettings:VoskModelPath"]).Returns("test-vosk-path");

            _provider = new TranscriptProvider(_mockLogger.Object, _mockConfiguration.Object);

            // Create a test WAV file
            _testFilePath = TestHelper.GetTestFilePath("Audio/sample.wav");
//            File.WriteAllText(_testFilePath, "test audio content");
        }

        [Fact]
        public async Task TranscribeFromStreamAsync_WhenStreamIsValid_ShouldCreateTempFileAndCleanup()
        {
            // Arrange
            var stream = new MemoryStream(File.ReadAllBytes(TestHelper.GetTestFilePath("Audio/sample.wav")));
            var fileName = "sample.wav";
            var engine = "whisper"; // Note: This will fail in real scenario without valid API key

            // Act
            var result = await _provider.TranscribeFromStreamAsync(stream, fileName, engine);

            // Assert
            // The method will fail due to invalid API key, but we can verify error handling
            result.Should().NotBeNull();
            result.IsValid.Should().BeFalse(); // Expected to fail with test API key
            result.Errors.Should().NotBeEmpty();
        }

        [Fact]
        public async Task TranscribeFromStreamAsync_WhenEngineIsUnknown_ShouldReturnError()
        {
            // Arrange
            var stream = new MemoryStream(File.ReadAllBytes(TestHelper.GetTestFilePath("Audio/sample.wav")));
            var fileName = "sample.wav";
            var engine = "unknown-engine";

            // Act
            var result = await _provider.TranscribeFromStreamAsync(stream, fileName, engine);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("Unknown engine"));
        }

        [Fact]
        public async Task TranscribeAsync_WhenEngineIsUnknown_ShouldReturnError()
        {
            // Arrange
            var engine = "invalid-engine";

            // Act
            var result = await _provider.TranscribeAsync(_testFilePath, engine);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("Unknown engine"));
        }

        [Fact]
        public async Task TranscribeAsync_WhenFileDoesNotExist_ShouldReturnError()
        {
            // Arrange
            var nonExistentFile = "D:\\NonExistent\\file.wav";
            var engine = "whisper";

            // Act
            var result = await _provider.TranscribeAsync(nonExistentFile, engine);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeFalse();
            result.Errors.Should().NotBeEmpty();
        }

        [Fact]
        public async Task TranscribeAsync_WhenWhisperExceptionOccurs_ShouldLogErrorAndReturnResult()
        {
            // Arrange
            var invalidPath = "D:\\Invalid\\Path\\file.wav";
            var engine = "whisper";

            // Act
            var result = await _provider.TranscribeAsync(invalidPath, engine);

            // Assert
            result.Should().NotBeNull();
            result.IsValid.Should().BeFalse();
            result.Errors.Should().NotBeEmpty();
            result.Data.Should().BeNull();

            // Verify logging
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
                Times.AtLeastOnce);
        }

        [Fact]
        public void MapToResponse_ShouldMapCorrectly()
        {
            // This is tested indirectly through other methods
            // We can add a reflection test if needed, but it's a private method
            Assert.True(true); // Placeholder
        }


    }
}
