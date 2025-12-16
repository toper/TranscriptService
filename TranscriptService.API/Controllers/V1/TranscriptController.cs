using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using TranscriptService.API.Models.Requests;
using TranscriptService.API.Models.Responses;
using TranscriptService.API.Provider;
using TranscriptService.Models;

namespace TranscriptService.API.Controllers.V1
{
    [ApiVersion("1")]
    public class TranscriptController : BaseController
    {
        private readonly ILogger<TranscriptController> _logger;
        private readonly ITranscriptProvider _transcriptProvider;
        private readonly IValidator<TranscriptFileRequest> _fileValidator;
        private readonly IValidator<TranscriptPathRequest> _pathValidator;

        public TranscriptController(
            ILogger<TranscriptController> logger,
            ITranscriptProvider transcriptProvider,
            IValidator<TranscriptFileRequest> fileValidator,
            IValidator<TranscriptPathRequest> pathValidator)
        {
            _logger = logger;
            _transcriptProvider = transcriptProvider;
            _fileValidator = fileValidator;
            _pathValidator = pathValidator;
        }

        /// <summary>
        /// Transcribe audio file uploaded via multipart/form-data
        /// </summary>
        /// <param name="request">Audio file and engine selection</param>
        /// <returns>Transcription result with word-level timestamps</returns>
        [HttpPost("file")]
        [RequestSizeLimit(100_000_000)] // 100MB
        [ProducesResponseType(typeof(Result<TranscriptResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(Result<TranscriptResponse>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(Result<TranscriptResponse>), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> TranscribeFile([FromForm] TranscriptFileRequest request)
        {
            var validationResult = await _fileValidator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                var result = new Result<TranscriptResponse>
                {
                    Errors = validationResult.Errors.Select(e => e.ErrorMessage).ToList()
                };
                return BadRequest(result);
            }

            _logger.LogInformation($"Processing file transcription: {request.AudioFile.FileName}, Engine: {request.Engine}");

            using var stream = request.AudioFile.OpenReadStream();
            var transcriptionResult = await _transcriptProvider.TranscribeFromStreamAsync(
                stream,
                request.AudioFile.FileName,
                request.Engine);

            if (!transcriptionResult.IsValid)
            {
                return BadRequest(transcriptionResult);
            }

            return Ok(transcriptionResult);
        }

        /// <summary>
        /// Transcribe audio file from server path
        /// </summary>
        /// <param name="request">File path and engine selection</param>
        /// <returns>Transcription result with word-level timestamps</returns>
        [HttpPost("path")]
        [ProducesResponseType(typeof(Result<TranscriptResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(Result<TranscriptResponse>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(Result<TranscriptResponse>), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> TranscribePath([FromBody] TranscriptPathRequest request)
        {
            var validationResult = await _pathValidator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                var result = new Result<TranscriptResponse>
                {
                    Errors = validationResult.Errors.Select(e => e.ErrorMessage).ToList()
                };
                return BadRequest(result);
            }

            _logger.LogInformation($"Processing path transcription: {request.FilePath}, Engine: {request.Engine}");

            var transcriptionResult = await _transcriptProvider.TranscribeAsync(
                request.FilePath,
                request.Engine);

            if (!transcriptionResult.IsValid)
            {
                return BadRequest(transcriptionResult);
            }

            return Ok(transcriptionResult);
        }
    }
}
