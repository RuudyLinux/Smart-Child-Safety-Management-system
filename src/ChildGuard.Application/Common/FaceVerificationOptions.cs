namespace ChildGuard.Application.Common;

/// <summary>Layered face verification (sessions, liveness challenge, thresholds), bound from the "FaceVerification" section.</summary>
public class FaceVerificationOptions
{
    /// <summary>An unfinished desk verification session expires after this many minutes.</summary>
    public int SessionMinutes { get; set; } = 10;

    /// <summary>A liveness challenge is valid this many seconds from issue and for a single submission.</summary>
    public int ChallengeSeconds { get; set; } = 30;

    /// <summary>A capture that arrives sooner than this after the challenge was issued cannot have followed the prompts.</summary>
    public double MinLiveSeconds { get; set; } = 2.0;

    /// <summary>Live attempts allowed per session; the session closes after this many failures.</summary>
    public int MaxLiveAttempts { get; set; } = 5;

    /// <summary>
    /// When true the live check also asks for random head turns. Off by default: the live check is then a short look at the
    /// camera judged on the anti-spoofing model, the same face staying in view and a plausible capture, which a held-up
    /// photo can still pass if the model is fooled.
    /// </summary>
    public bool RequireHeadTurn { get; set; }

    public int MinFrames { get; set; } = 6;
    public int MaxFrames { get; set; } = 16;

    /// <summary>Share of sampled frames the passive model must call real. Not calibrated on real webcams yet.</summary>
    public double PassiveMinRealFraction { get; set; } = 0.8;

    /// <summary>Mean passive P(real) required. Not calibrated on real webcams yet.</summary>
    public double PassiveMinMeanReal { get; set; } = 0.6;

    /// <summary>
    /// If two different guardians both reach the match threshold within this similarity of each other the
    /// identity is Inconclusive. 0 turns it off. It never turns a NoMatch into a Match. Awaiting an approved value.
    /// </summary>
    public double AmbiguityMargin { get; set; }

    public TimeSpan SessionLifetime => TimeSpan.FromMinutes(Math.Max(1, SessionMinutes));
    public TimeSpan ChallengeLifetime => TimeSpan.FromSeconds(Math.Max(5, ChallengeSeconds));
}
