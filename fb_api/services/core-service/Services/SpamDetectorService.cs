using System.Text.RegularExpressions;
using FbApi.Contracts.Models;

namespace FbApi.CoreService.Services;

public class SpamDetectorResult
{
    public bool IsSpam { get; set; }
    public string SpamType { get; set; } = "none";
}

public interface ISpamDetectorService
{
    SpamDetectorResult Detect(NormalizedEvent evt);
}

public class SpamDetectorService : ISpamDetectorService
{
    private static readonly Regex UrlPattern = new(@"https?://\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly string[] ScamKeywords = ["free money", "click here", "buy now", "subscribe", "winner", "congratulations", "prize", "lottery", "crypto", "investment opportunity"];
    private static readonly Regex RepeatedContentPattern = new(@"(.{5,}?)\1{3,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public SpamDetectorResult Detect(NormalizedEvent evt)
    {
        var msg = evt.Message;

        if (UrlPattern.IsMatch(msg))
            return new SpamDetectorResult { IsSpam = true, SpamType = "link" };

        if (RepeatedContentPattern.IsMatch(msg) || HasRepeatedPhrase(msg))
            return new SpamDetectorResult { IsSpam = true, SpamType = "repeated" };

        var lowerMsg = msg.ToLowerInvariant();
        foreach (var keyword in ScamKeywords)
        {
            if (lowerMsg.Contains(keyword))
                return new SpamDetectorResult { IsSpam = true, SpamType = "scam" };
        }

        return new SpamDetectorResult { IsSpam = false, SpamType = "none" };
    }

    private static bool HasRepeatedPhrase(string message)
    {
        var words = Regex.Matches(message.ToLowerInvariant(), @"\b[\p{L}\p{N}]+\b")
            .Select(match => match.Value)
            .ToArray();

        for (var phraseLength = 1; phraseLength <= Math.Min(4, words.Length / 3); phraseLength++)
        {
            var consecutiveRepeats = 1;
            for (var i = phraseLength; i + phraseLength <= words.Length; i += phraseLength)
            {
                var previous = words.Skip(i - phraseLength).Take(phraseLength);
                var current = words.Skip(i).Take(phraseLength);

                if (previous.SequenceEqual(current))
                {
                    consecutiveRepeats++;
                    if (consecutiveRepeats >= 3)
                        return true;
                }
                else
                {
                    consecutiveRepeats = 1;
                }
            }
        }

        return false;
    }
}
