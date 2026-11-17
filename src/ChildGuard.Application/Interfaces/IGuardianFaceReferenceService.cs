using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;

namespace ChildGuard.Application.Interfaces;

public interface IGuardianFaceReferenceService
{
    Task<Result<GuardianFaceReferencesDto>> ListAsync(int guardianId, CancellationToken ct = default);

    /// <summary>
    /// Validates the photo on its own (Layer 1 plus the registration rules), stores its embedding as a
    /// reference, and skips a near-identical duplicate. The first face a guardian registers becomes the primary one.
    /// </summary>
    Task<Result<AddFaceReferenceResultDto>> AddAsync(int guardianId, AddFaceReferenceRequest request, FaceActor actor, CancellationToken ct = default);

    /// <summary>Deactivates an additional reference. The primary face is replaced through the existing registration, not removed here.</summary>
    Task<Result> RemoveAsync(int guardianId, int referenceId, FaceActor actor, CancellationToken ct = default);

    /// <summary>Every active reference, readable form (primary as id 0). Internal use only; never returned to a client.</summary>
    Task<Result<IReadOnlyList<ReferenceEmbedding>>> LoadEmbeddingsAsync(int guardianId, CancellationToken ct = default);
}
