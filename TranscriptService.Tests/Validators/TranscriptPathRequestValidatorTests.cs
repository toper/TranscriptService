using FluentAssertions;
using TranscriptService.API.Models.Requests;
using TranscriptService.API.Validators;

namespace TranscriptService.Tests.Validators
{
    public class TranscriptPathRequestValidatorTests
    {
        private readonly TranscriptPathRequestValidator _validator;
        private readonly string _testFilePath;

        public TranscriptPathRequestValidatorTests()
        {
            _validator = new TranscriptPathRequestValidator();

            // Create a temporary test file
            _testFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");
            File.WriteAllText(_testFilePath, "test");
        }

        [Fact]
        public async Task Validate_WhenFilePathIsEmpty_ShouldHaveValidationError()
        {
            // Arrange
            var request = new TranscriptPathRequest
            {
                FilePath = "",
                Engine = "whisper"
            };

            // Act
            var result = await _validator.ValidateAsync(request);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "File path is required");
        }

        [Fact]
        public async Task Validate_WhenFileDoesNotExist_ShouldHaveValidationError()
        {
            // Arrange
            var request = new TranscriptPathRequest
            {
                FilePath = "D:\\NonExistent\\file.wav",
                Engine = "whisper"
            };

            // Act
            var result = await _validator.ValidateAsync(request);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "File does not exist");
        }

        [Theory]
        [InlineData(".mp3")]
        [InlineData(".txt")]
        [InlineData(".pdf")]
        public async Task Validate_WhenFileExtensionIsNotWav_ShouldHaveValidationError(string extension)
        {
            // Arrange
            var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{extension}");
            File.WriteAllText(tempFile, "test");

            try
            {
                var request = new TranscriptPathRequest
                {
                    FilePath = tempFile,
                    Engine = "whisper"
                };

                // Act
                var result = await _validator.ValidateAsync(request);

                // Assert
                result.IsValid.Should().BeFalse();
                result.Errors.Should().Contain(e => e.ErrorMessage == "Only WAV files are supported");
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task Validate_WhenEngineIsEmpty_ShouldHaveValidationError()
        {
            // Arrange
            var request = new TranscriptPathRequest
            {
                FilePath = _testFilePath,
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
            var request = new TranscriptPathRequest
            {
                FilePath = _testFilePath,
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
            var request = new TranscriptPathRequest
            {
                FilePath = _testFilePath,
                Engine = engine
            };

            // Act
            var result = await _validator.ValidateAsync(request);

            // Assert
            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }

        [Fact]
        public void Cleanup_TestFile()
        {
            // Cleanup test file
            if (File.Exists(_testFilePath))
            {
                File.Delete(_testFilePath);
            }
            Assert.True(true);
        }
    }
}
