using FluentValidation;
using TranscriptService.API.Models.Requests;

namespace TranscriptService.API.Validators
{
    public class TranscriptFileRequestValidator : AbstractValidator<TranscriptFileRequest>
    {
        public TranscriptFileRequestValidator()
        {
            RuleFor(x => x.AudioFile)
                .NotNull().WithMessage("Audio file is required");

            RuleFor(x => x.AudioFile.Length)
                .LessThanOrEqualTo(100 * 1024 * 1024) // 100MB
                .WithMessage("File size must not exceed 100MB")
                .When(x => x.AudioFile != null);

            RuleFor(x => x.AudioFile.ContentType)
                .Must(ct => ct == "audio/wav" || ct == "audio/x-wav" || ct == "audio/wave")
                .WithMessage("Only WAV audio files are supported")
                .When(x => x.AudioFile != null);

            RuleFor(x => x.Engine)
                .NotEmpty().WithMessage("Engine is required")
                .Must(e => e.ToLower() == "whisper" || e.ToLower() == "vosk")
                .WithMessage("Engine must be 'whisper' or 'vosk'");
        }
    }
}
