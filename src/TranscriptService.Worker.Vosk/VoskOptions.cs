using System.ComponentModel.DataAnnotations;
using TranscriptService.Worker.Core;

namespace TranscriptService.Worker.Vosk;

/// <summary>Section <c>Engine</c> for the Vosk worker.</summary>
public sealed class VoskOptions : EngineOptions, IValidatableObject
{
    /// <summary>
    /// Language of the deployed model. A Vosk model recognizes exactly one language, and the model
    /// files do not say which, so the deployment states it.
    /// </summary>
    [Required]
    [RegularExpression("^[a-z]{2}$")]
    public string ModelLanguage { get; set; } = "pl";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Device != ComputeDevice.Cpu)
        {
            yield return new ValidationResult("Vosk runs on the CPU only; set Engine:Device to Cpu.", [nameof(Device)]);
        }
    }
}
