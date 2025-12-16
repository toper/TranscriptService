using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using TranscriptService.API.Models.Requests;
using TranscriptService.API.Validators;

namespace TranscriptService.Tests.Validators
{
    public class TranscriptFileRequestValidatorTests
    {
        private readonly TranscriptFileRequestValidator _validator;

        public TranscriptFileRequestValidatorTests()
        {
            _validator = new TranscriptFileRequestValidator();
        }

        [Fact]
        public async Task Validate_WhenAudioFileIsNull_ShouldHaveValidationError()
        {
            // Arrange
            var request = new TranscriptFileRequest
            {
                AudioFile = null!,
                Engine = "whisper"
            };

            // Act
            var result = await _validator.ValidateAsync(request);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "Audio file is required");
        }

        [Fact]
        public async Task Validate_WhenFileSizeExceeds100MB_ShouldHaveValidationError()
        {
            // Arrange
            var mockFile = new Mock<IFormFile>();
            mockFile.Setup(f => f.Length).Returns(101 * 1024 * 1024); // 101MB
            mockFile.Setup(f => f.ContentType).Returns("audio/wav");

            var request = new TranscriptFileRequest
            {
                AudioFile = mockFile.Object,
                Engine = "whisper"
            };

            // Act
            var result = await _validator.ValidateAsync(request);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "File size must not exceed 100MB");
        }

        [Fact]
        public async Task Validate_WhenContentTypeIsNotWav_ShouldHaveValidationError()
        {
            // Arrange
            var mockFile = new Mock<IFormFile>();
            mockFile.Setup(f => f.Length).Returns(10 * 1024 * 1024); // 10MB
            mockFile.Setup(f => f.ContentType).Returns("audio/mp3");

            var request = new TranscriptFileRequest
            {
                AudioFile = mockFile.Object,
                Engine = "whisper"
            };

            // Act
            var result = await _validator.ValidateAsync(request);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "Only WAV audio files are supported");
        }

        [Fact]
        public async Task Validate_WhenEngineIsEmpty_ShouldHaveValidationError()
        {
            // Arrange
            var mockFile = new Mock<IFormFile>();
            mockFile.Setup(f => f.Length).Returns(10 * 1024 * 1024);
            mockFile.Setup(f => f.ContentType).Returns("audio/wav");

            var request = new TranscriptFileRequest
            {
                AudioFile = mockFile.Object,
                Engine = ""
            };

            // Act
            var result = await _validator.ValidateAsync(request);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "Engine is required");
        }

        [Theory]
        [InlineData("unknown")]
        [InlineData("google")]
        [InlineData("azure")]
        public async Task Validate_WhenEngineIsInvalid_ShouldHaveValidationError(string engine)
        {
            // Arrange
            var mockFile = new Mock<IFormFile>();
            mockFile.Setup(f => f.Length).Returns(10 * 1024 * 1024);
            mockFile.Setup(f => f.ContentType).Returns("audio/wav");

            var request = new TranscriptFileRequest
            {
                AudioFile = mockFile.Object,
                Engine = engine
            };

            // Act
            var result = await _validator.ValidateAsync(request);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "Engine must be 'whisper' or 'vosk'");
        }

        [Theory]
        [InlineData("whisper")]
        [InlineData("vosk")]
        [InlineData("WHISPER")]
        [InlineData("Vosk")]
        public async Task Validate_WhenAllFieldsAreValid_ShouldPass(string engine)
        {
            // Arrange
            var mockFile = new Mock<IFormFile>();
            mockFile.Setup(f => f.Length).Returns(10 * 1024 * 1024); // 10MB
            mockFile.Setup(f => f.ContentType).Returns("audio/wav");

            var request = new TranscriptFileRequest
            {
                AudioFile = mockFile.Object,
                Engine = engine
            };

            // Act
            var result = await _validator.ValidateAsync(request);

            // Assert
            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }

        [Theory]
        [InlineData("audio/wav")]
        [InlineData("audio/x-wav")]
        [InlineData("audio/wave")]
        public async Task Validate_WhenContentTypeIsValidWavVariant_ShouldPass(string contentType)
        {
            // Arrange
            var mockFile = new Mock<IFormFile>();
            mockFile.Setup(f => f.Length).Returns(10 * 1024 * 1024);
            mockFile.Setup(f => f.ContentType).Returns(contentType);

            var request = new TranscriptFileRequest
            {
                AudioFile = mockFile.Object,
                Engine = "whisper"
            };

            // Act
            var result = await _validator.ValidateAsync(request);

            // Assert
            result.IsValid.Should().BeTrue();
        }
    }
}
