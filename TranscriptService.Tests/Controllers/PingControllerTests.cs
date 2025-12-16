using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using TranscriptService.API.Controllers.Neutral;

namespace TranscriptService.Tests.Controllers
{
    public class PingControllerTests
    {
        private readonly PingController _controller;

        public PingControllerTests()
        {
            _controller = new PingController();
        }

        [Fact]
        public void Ping_ShouldReturnOkWithPongMessage()
        {
            // Act
            var result = _controller.Ping();

            // Assert
            result.Should().BeOfType<OkObjectResult>();
            var okResult = result as OkObjectResult;
            okResult!.Value.Should().Be("Pong");
        }

        [Fact]
        public void Ping_ShouldReturn200StatusCode()
        {
            // Act
            var result = _controller.Ping() as OkObjectResult;

            // Assert
            result!.StatusCode.Should().Be(200);
        }
    }
}
