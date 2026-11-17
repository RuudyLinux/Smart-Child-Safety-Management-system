namespace ChildGuard.Application.Services.FaceVerification;

/// <summary>Plain-language messages for the desk. Reason codes are stable; the wording lives only here.</summary>
public static class FaceVerificationMessages
{
    private static readonly Dictionary<string, string> Reasons = new(StringComparer.Ordinal)
    {
        ["PASSIVE_SPOOF_SUSPECTED"] = "Possible spoofing detected: a photo or a screen may have been shown to the camera.",
        ["FACE_CHANGED"] = "A different face appeared during the check. Possible spoofing detected.",
        ["REPLAYED_FRAME"] = "This capture matches an earlier one. Possible spoofing detected.",
        ["DUPLICATE_FRAMES"] = "The camera frames repeat. Possible spoofing detected.",
        ["CHALLENGE_NOT_COMPLETED"] = "The head movements were not completed as asked. Follow the on-screen prompts and try again.",
        ["WRONG_DIRECTION"] = "The head was turned the wrong way. Follow the on-screen prompts and try again.",
        ["FACE_LOST"] = "The face was not visible for the whole check. Stay in view and try again.",
        ["TOO_FEW_USABLE_FRAMES"] = "Not enough clear camera frames were received. Try again.",
        ["MULTIPLE_FACES"] = "More than one face was in view. Only the guardian should be in the frame.",
        ["NO_NEUTRAL_FRAME"] = "Start by looking straight at the camera, then follow the prompts.",
        ["BAD_TIMING"] = "The check was too quick or too slow. Try again.",
        ["TOO_FAST"] = "The check was too quick. Wait for each prompt and try again.",
        ["PASSIVE_UNAVAILABLE"] = "The live-person check could not run (face service problem). Try again, or verify the guardian another way.",
        ["EMBEDDING_FAILED"] = "The live-person check could not be completed (face service problem). Try again.",
        ["CONTINUITY_UNKNOWN"] = "The live-person check could not confirm the same face throughout. Try again.",
        ["DIFFERS_FROM_PHOTO"] = "The person at the camera is not the guardian identified from the uploaded photo."
    };

    public static IEnumerable<string> ForReasons(IEnumerable<string> codes) =>
        codes.Where(Reasons.ContainsKey).Select(c => Reasons[c]).Distinct();

    public static string ForOutcome(DecisionOutcome outcome, IReadOnlyCollection<string> failureMessages) => outcome switch
    {
        DecisionOutcome.PickupEligible => "All checks passed. Review the result and approve or reject the pickup.",
        DecisionOutcome.RetryCapture => failureMessages.FirstOrDefault() ?? "The image is not suitable. Retake it.",
        DecisionOutcome.NotRecognised => "No authorised guardian for this student matches the face. Do not release the student.",
        DecisionOutcome.ReviewRequired => "The match could not be settled between guardians. Retry, or review manually. The pickup cannot be approved from this result.",
        DecisionOutcome.Unauthorised => "Guardian not authorised for this student. Do not release the student.",
        DecisionOutcome.LiveVerificationRequired => "Identity matched, but liveness verification is required. Use the live camera check.",
        DecisionOutcome.LivenessFailed => failureMessages.FirstOrDefault() ?? "The live-person check did not pass.",
        _ => "The verification did not pass."
    };
}
