using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;

namespace ChildGuard.Application.Interfaces;

/// <summary>A liveness challenge: a random single-use token and the head movements to ask for.</summary>
public sealed record LiveChallenge(string Token, IReadOnlyList<string> Steps);

public interface ILiveChallengeGenerator
{
    LiveChallenge Create();
}

/// <summary>
/// The desk's layered face verification. A session belongs to the admin who opened it and to one student;
/// everything is judged on the server from the raw images, and the client only supplies bytes.
/// </summary>
public interface IVerificationSessionService
{
    /// <summary>Opens a session for a student and lists the guardians the desk may verify for them.</summary>
    Task<Result<VerificationSessionDto>> StartAsync(int studentId, int adminId, CancellationToken ct = default);

    /// <summary>
    /// Image quality, face detection, identity among this student's guardians, and authorisation, for a webcam
    /// still or a gallery file. Never establishes liveness, so the result is at best "live verification required".
    /// </summary>
    Task<Result<VerificationAttemptResultDto>> IdentifyAsync(int sessionId, int adminId, SessionIdentifyRequest request, CancellationToken ct = default);

    /// <summary>Issues a fresh random head-movement challenge (replacing any earlier one) valid for a short time and one submission.</summary>
    Task<Result<LiveChallengeDto>> IssueChallengeAsync(int sessionId, int adminId, CancellationToken ct = default);

    /// <summary>
    /// Judges a webcam burst against the challenge: quality, identity, authorisation and liveness together. Only when
    /// every layer passes does it create a pending verification for an administrator to approve or reject.
    /// </summary>
    Task<Result<VerificationAttemptResultDto>> LiveVerifyAsync(int sessionId, int adminId, LiveVerifyRequest request, CancellationToken ct = default);
}
