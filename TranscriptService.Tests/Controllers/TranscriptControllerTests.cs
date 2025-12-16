using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using TranscriptService.API.Controllers.V1;
using TranscriptService.API.Models.Requests;
using TranscriptService.API.Models.Responses;
using TranscriptService.API.Provider;
using TranscriptService.Models;

namespace TranscriptService.Tests.Controllers
{
    public class TranscriptControllerTests
    {
        private readonly Mock<ILogger<TranscriptController>> _mockLogger;
        private readonly Mock<ITranscriptProvider> _mockProvider;
        private readonly Mock<IValidator<TranscriptFileRequest>> _mockFileValidator;
        private readonly Mock<IValidator<TranscriptPathRequest>> _mockPathValidator;
        private readonly TranscriptController _controller;

        public TranscriptControllerTests()
        {
            _mockLogger = new Mock<ILogger<TranscriptController>>();
            _mockProvider = new Mock<ITranscriptProvider>();
            _mockFileValidator = new Mock<IValidator<TranscriptFileRequest>>();
            _mockPathValidator = new Mock<IValidator<TranscriptPathRequest>>();

            _controller = new TranscriptController(
                _mockLogger.Object,
                _mockProvider.Object,
                _mockFileValidator.Object,
                _mockPathValidator.Object);
        }

        #region TranscribeFile Tests

        [Fact]
        public async Task TranscribeFile_WhenValidationFails_ShouldReturnBadRequest()
        {
            // Arrange
            var mockFile = new Mock<IFormFile>();
            var request = new TranscriptFileRequest
            {
                AudioFile = mockFile.Object,
                Engine = "whisper"
            };

            var validationResult = new ValidationResult(new[]
            {
                new ValidationFailure("AudioFile", "File is too large")
            });

            _mockFileValidator.Setup(v => v.ValidateAsync(request, default))
                .ReturnsAsync(validationResult);

            // Act
            var result = await _controller.TranscribeFile(request);

            // Assert
            result.Should().BeOfType<BadRequestObjectResult>();
            var badRequestResult = result as BadRequestObjectResult;
            badRequestResult!.Value.Should().BeOfType<Result<TranscriptResponse>>();

            var resultValue = badRequestResult.Value as Result<TranscriptResponse>;
            resultValue!.IsValid.Should().BeFalse();
            resultValue.Errors.Should().Contain("File is too large");
        }

        [Fact]
        public async Task TranscribeFile_WhenProviderReturnsError_ShouldReturnBadRequest()
        {
            // Arrange
            var mockFile = new Mock<IFormFile>();
            mockFile.Setup(f => f.OpenReadStream()).Returns(new MemoryStream());
            mockFile.Setup(f => f.FileName).Returns("test.wav");

            var request = new TranscriptFileRequest
            {
                AudioFile = mockFile.Object,
                Engine = "whisper"
            };

            _mockFileValidator.Setup(v => v.ValidateAsync(request, default))
                .ReturnsAsync(new ValidationResult());

            var providerResult = new Result<TranscriptResponse>();
            providerResult.Errors.Add("Transcription failed");

            _mockProvider.Setup(p => p.TranscribeFromStreamAsync(
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .ReturnsAsync(providerResult);

            // Act
            var result = await _controller.TranscribeFile(request);

            // Assert
            result.Should().BeOfType<BadRequestObjectResult>();
            var badRequestResult = result as BadRequestObjectResult;
            var resultValue = badRequestResult!.Value as Result<TranscriptResponse>;
            resultValue!.IsValid.Should().BeFalse();
            resultValue.Errors.Should().Contain("Transcription failed");
        }

        [Fact]
        public async Task TranscribeFile_WhenSuccessful_ShouldReturnOk()
        {
            // Arrange
            var mockFile = new Mock<IFormFile>();
            mockFile.Setup(f => f.OpenReadStream()).Returns(new MemoryStream());
            mockFile.Setup(f => f.FileName).Returns("test.wav");

            var request = new TranscriptFileRequest
            {
                AudioFile = mockFile.Object,
                Engine = "whisper"
            };

            _mockFileValidator.Setup(v => v.ValidateAsync(request, default))
                .ReturnsAsync(new ValidationResult());

            var expectedResponse = new TranscriptResponse
            {
                FileName = "test.wav",
                FullText = "Test transcription",
                Words = new List<WordTimestampDto>(),
                Engine = "whisper",
                ProcessedAt = DateTime.UtcNow
            };

            var providerResult = new Result<TranscriptResponse>
            {
                Data = expectedResponse
            };

            _mockProvider.Setup(p => p.TranscribeFromStreamAsync(
                    It.IsAny<Stream>(),
                    "test.wav",
                    "whisper"))
                .ReturnsAsync(providerResult);

            // Act
            var result = await _controller.TranscribeFile(request);

            // Assert
            result.Should().BeOfType<OkObjectResult>();
            var okResult = result as OkObjectResult;
            var resultValue = okResult!.Value as Result<TranscriptResponse>;
            resultValue!.IsValid.Should().BeTrue();
            resultValue.Data.Should().NotBeNull();
            resultValue.Data.FileName.Should().Be("test.wav");
        }

        #endregion

        #region TranscribePath Tests

        [Fact]
        public async Task TranscribePath_WhenValidationFails_ShouldReturnBadRequest()
        {
            // Arrange
            var request = new TranscriptPathRequest
            {
                FilePath = "invalid.txt",
                Engine = "whisper"
            };

            var validationResult = new ValidationResult(new[]
            {
                new ValidationFailure("FilePath", "File does not exist")
            });

            _mockPathValidator.Setup(v => v.ValidateAsync(request, default))
                .ReturnsAsync(validationResult);

            // Act
            var result = await _controller.TranscribePath(request);

            // Assert
            result.Should().BeOfType<BadRequestObjectResult>();
            var badRequestResult = result as BadRequestObjectResult;
            var resultValue = badRequestResult!.Value as Result<TranscriptResponse>;
            resultValue!.IsValid.Should().BeFalse();
            resultValue.Errors.Should().Contain("File does not exist");
        }

        [Fact]
        public async Task TranscribePath_WhenProviderReturnsError_ShouldReturnBadRequest()
        {
            // Arrange
            var request = new TranscriptPathRequest
            {
                FilePath = "D:\\test.wav",
                Engine = "vosk"
            };

            _mockPathValidator.Setup(v => v.ValidateAsync(request, default))
                .ReturnsAsync(new ValidationResult());

            var providerResult = new Result<TranscriptResponse>();
            providerResult.Errors.Add("Vosk model not found");

            _mockProvider.Setup(p => p.TranscribeAsync("D:\\test.wav", "vosk"))
                .ReturnsAsync(providerResult);

            // Act
            var result = await _controller.TranscribePath(request);

            // Assert
            result.Should().BeOfType<BadRequestObjectResult>();
            var badRequestResult = result as BadRequestObjectResult;
            var resultValue = badRequestResult!.Value as Result<TranscriptResponse>;
            resultValue!.IsValid.Should().BeFalse();
            resultValue.Errors.Should().Contain("Vosk model not found");
        }

        [Fact]
        public async Task TranscribePath_WhenSuccessful_ShouldReturnOk()
        {
            // Arrange
            var request = new TranscriptPathRequest
            {
                FilePath = "D:\\audio\\test.wav",
                Engine = "vosk"
            };

            _mockPathValidator.Setup(v => v.ValidateAsync(request, default))
                .ReturnsAsync(new ValidationResult());

            var expectedResponse = new TranscriptResponse
            {
                FileName = "test.wav",
                FullText = "Vosk transcription result",
                Words = new List<WordTimestampDto>
                {
                    new WordTimestampDto { Word = "Vosk", StartTime = 0.0f, EndTime = 0.5f }
                },
                Engine = "vosk",
                ProcessedAt = DateTime.UtcNow
            };

            var providerResult = new Result<TranscriptResponse>
            {
                Data = expectedResponse
            };

            _mockProvider.Setup(p => p.TranscribeAsync("D:\\audio\\test.wav", "vosk"))
                .ReturnsAsync(providerResult);

            // Act
            var result = await _controller.TranscribePath(request);

            // Assert
            result.Should().BeOfType<OkObjectResult>();
            var okResult = result as OkObjectResult;
            var resultValue = okResult!.Value as Result<TranscriptResponse>;
            resultValue!.IsValid.Should().BeTrue();
            resultValue.Data.Should().NotBeNull();
            resultValue.Data.FullText.Should().Be("Vosk transcription result");
            resultValue.Data.Words.Should().HaveCount(1);
        }

        [Fact]
        public async Task TranscribePath_ShouldLogInformation()
        {
            // Arrange
            var request = new TranscriptPathRequest
            {
                FilePath = "D:\\test.wav",
                Engine = "whisper"
            };

            _mockPathValidator.Setup(v => v.ValidateAsync(request, default))
                .ReturnsAsync(new ValidationResult());

            _mockProvider.Setup(p => p.TranscribeAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new Result<TranscriptResponse> { Data = new TranscriptResponse() });

            // Act
            await _controller.TranscribePath(request);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Processing path transcription")),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
                Times.Once);
        }

        #endregion
    }
}
