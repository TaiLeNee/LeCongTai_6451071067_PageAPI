using System;

namespace FbApi.Contracts.Models;

public class AiAnalysisResult
{
    public string Intent { get; set; } = "neutral";
    public string Sentiment { get; set; } = "neutral";
    public double Confidence { get; set; }
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    public string? Reason { get; set; }
}
