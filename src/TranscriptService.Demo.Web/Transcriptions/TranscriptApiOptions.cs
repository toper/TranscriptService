using System.ComponentModel.DataAnnotations;

namespace TranscriptService.Demo.Web.Transcriptions;

/// <summary>Section <c>TranscriptApi</c>: how the demo reaches the API and how the API reaches back.</summary>
public sealed class TranscriptApiOptions
{
    public const string SectionName = "TranscriptApi";

    [Required]
    public Uri? BaseUrl { get; set; }

    [Required]
    [MinLength(16)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Same secret as the API's <c>Api:CallbackSigningSecret</c>; verifies callbacks.</summary>
    [Required]
    [MinLength(32)]
    public string CallbackSigningSecret { get; set; } = string.Empty;

    /// <summary>Address of this demo as the API sees it, e.g. <c>http://demo-web:8080</c> inside compose.</summary>
    [Required]
    public Uri? PublicBaseUrl { get; set; }

    [Required]
    [RegularExpression("^[a-z]{2}$")]
    public string Language { get; set; } = "pl";
}
