using System.Security.Cryptography;
using ChildGuard.Application.Interfaces;

namespace ChildGuard.Application.Services.FaceVerification;

/// <summary>A 256-bit random token and a random two-step head-turn sequence (four possible sequences).</summary>
public sealed class SecureLiveChallengeGenerator : ILiveChallengeGenerator
{
    private static readonly string[] Moves = ["turn_left", "turn_right"];

    public LiveChallenge Create()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return new LiveChallenge(token, [Moves[RandomNumberGenerator.GetInt32(2)], Moves[RandomNumberGenerator.GetInt32(2)]]);
    }
}
