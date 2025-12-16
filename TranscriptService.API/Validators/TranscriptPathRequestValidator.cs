using FluentValidation;
using TranscriptService.API.Models.Requests;

namespace TranscriptService.API.Validators
{
    public class TranscriptPathRequestValidator : AbstractValidator<TranscriptPathRequest>
    {
        public TranscriptPathRequestValidator()
        {
            RuleFor(x => x.FilePath)
                .NotEmpty().WithMessage("File path is required")
                .Must(File.Exists).WithMessage("File does not exist")
                .Must(path => Path.GetExtension(path).ToLower() == ".wav")
                .WithMessage("Only WAV files are supported");

            RuleFor(x => x.Engine)
                .NotEmpty().WithMessage("Engine is required")
                .Must(e => e.ToLower() == "whisper" || e.ToLower() == "vosk")
                .WithMessage("Engine must be 'whisper' or 'vosk'");
        }
    }
}
