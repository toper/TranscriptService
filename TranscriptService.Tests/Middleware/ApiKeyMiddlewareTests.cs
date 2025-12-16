using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Text;
using TranscriptService.API.Middleware;

namespace TranscriptService.Tests.Middleware
{
    public class ApiKeyMiddlewareTests
    {
        private readonly Mock<IConfiguration> _mockConfiguration;
        private readonly Mock<RequestDelegate> _mockNext;
        private const string ValidApiKey = "test-api-key-12345";

        public ApiKeyMiddlewareTests()
        {
            _mockConfiguration = new Mock<IConfiguration>();
            _mockConfiguration.Setup(c => c["AppSettings:ApiKey"]).Returns(ValidApiKey);
            _mockNext = new Mock<RequestDelegate>();
        }

        [Fact]
        public async Task InvokeAsync_WhenPathIsPing_ShouldSkipValidation()
        {
            // Arrange
            var context = new DefaultHttpContext();
            context.Request.Path = "/ping";
            var middleware = new ApiKeyMiddleware(_mockNext.Object, _mockConfiguration.Object);

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            _mockNext.Verify(next => next(It.IsAny<HttpContext>()), Times.Once);
            context.Response.StatusCode.Should().Be(200);
        }

        [Fact]
        public async Task InvokeAsync_WhenPathIsHealth_ShouldSkipValidation()
        {
            // Arrange
            var context = new DefaultHttpContext();
            context.Request.Path = "/health";
            var middleware = new ApiKeyMiddleware(_mockNext.Object, _mockConfiguration.Object);

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            _mockNext.Verify(next => next(It.IsAny<HttpContext>()), Times.Once);
            context.Response.StatusCode.Should().Be(200);
        }

        [Fact]
        public async Task InvokeAsync_WhenApiKeyHeaderIsMissing_ShouldReturn401()
        {
            // Arrange
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/v1/transcript/file";
            context.Response.Body = new MemoryStream();
            var middleware = new ApiKeyMiddleware(_mockNext.Object, _mockConfiguration.Object);

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            context.Response.StatusCode.Should().Be(401);
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
            responseBody.Should().Be("API Key is missing");
            _mockNext.Verify(next => next(It.IsAny<HttpContext>()), Times.Never);
        }

        [Fact]
        public async Task InvokeAsync_WhenApiKeyIsInvalid_ShouldReturn401()
        {
            // Arrange
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/v1/transcript/file";
            context.Request.Headers["X-API-Key"] = "invalid-key";
            context.Response.Body = new MemoryStream();
            var middleware = new ApiKeyMiddleware(_mockNext.Object, _mockConfiguration.Object);

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            context.Response.StatusCode.Should().Be(401);
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
            responseBody.Should().Be("Unauthorized client");
            _mockNext.Verify(next => next(It.IsAny<HttpContext>()), Times.Never);
        }

        [Fact]
        public async Task InvokeAsync_WhenApiKeyIsValid_ShouldCallNext()
        {
            // Arrange
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/v1/transcript/file";
            context.Request.Headers["X-API-Key"] = ValidApiKey;
            var middleware = new ApiKeyMiddleware(_mockNext.Object, _mockConfiguration.Object);

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            _mockNext.Verify(next => next(It.IsAny<HttpContext>()), Times.Once);
        }

        [Fact]
        public async Task InvokeAsync_WhenConfiguredApiKeyIsNull_ShouldReturn401()
        {
            // Arrange
            var mockConfig = new Mock<IConfiguration>();
            mockConfig.Setup(c => c["AppSettings:ApiKey"]).Returns((string)null!);

            var context = new DefaultHttpContext();
            context.Request.Path = "/api/v1/transcript/file";
            context.Request.Headers["X-API-Key"] = "some-key";
            context.Response.Body = new MemoryStream();
            var middleware = new ApiKeyMiddleware(_mockNext.Object, mockConfig.Object);

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            context.Response.StatusCode.Should().Be(401);
            _mockNext.Verify(next => next(It.IsAny<HttpContext>()), Times.Never);
        }

        [Theory]
        [InlineData("/api/v1/transcript/path")]
        [InlineData("/api/v1/transcript/file")]
        [InlineData("/error")]
        public async Task InvokeAsync_WhenProtectedPathWithValidKey_ShouldCallNext(string path)
        {
            // Arrange
            var context = new DefaultHttpContext();
            context.Request.Path = path;
            context.Request.Headers["X-API-Key"] = ValidApiKey;
            var middleware = new ApiKeyMiddleware(_mockNext.Object, _mockConfiguration.Object);

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            _mockNext.Verify(next => next(It.IsAny<HttpContext>()), Times.Once);
        }
    }
}
