import 'package:flutter_test/flutter_test.dart';
import 'package:guardian_app/models/models.dart';

void main() {
  trustedDeviceModelTests();
  test('parses the reference list the API returns (metadata only)', () {
    final refs = FaceReferences.fromJson({
      'references': [
        {'id': 0, 'source': 'Primary', 'label': null, 'createdAt': '2026-11-12T10:00:00Z', 'quality': null},
        {'id': 4, 'source': 'Additional', 'label': 'Outdoor', 'createdAt': '2026-11-13T08:30:00Z',
         'quality': {'sharpness': 120.5, 'brightness': 150.0, 'facePx': 220, 'yawDegEstimate': 3.2}},
      ],
      'maxReferences': 5,
      'canAdd': true,
    });

    expect(refs.references.length, 2);
    expect(refs.references.first.isPrimary, isTrue);
    expect(refs.references.first.label, isNull);
    expect(refs.references[1].isPrimary, isFalse);
    expect(refs.references[1].id, 4);
    expect(refs.references[1].label, 'Outdoor');
    expect(refs.maxReferences, 5);
    expect(refs.canAdd, isTrue);
  });

  test('tolerates missing fields from an older or newer server', () {
    final refs = FaceReferences.fromJson({});
    expect(refs.references, isEmpty);
    expect(refs.maxReferences, 5);
    expect(refs.canAdd, isTrue);
  });

  test('an analysis that did not pass carries the messages to show', () {
    final a = FaceImageAnalysis.fromJson({
      'passed': false,
      'faceCount': 2,
      'issues': ['MULTIPLE_FACES'],
      'messages': ['More than one face was detected.'],
    });
    expect(a.passed, isFalse);
    expect(a.faceCount, 2);
    expect(a.issues, ['MULTIPLE_FACES']);
    expect(a.messages.single, contains('More than one face'));
  });

  test('an analysis is never treated as passed unless the server says so', () {
    expect(FaceImageAnalysis.fromJson({}).passed, isFalse);
    expect(FaceImageAnalysis.fromJson({'passed': 'true'}).passed, isFalse);
  });

  test('add result distinguishes added from duplicate', () {
    final dup = AddFaceReferenceResult.fromJson({'added': false, 'duplicate': true, 'message': 'Already there.'});
    expect(dup.added, isFalse);
    expect(dup.duplicate, isTrue);
    expect(dup.message, 'Already there.');
  });

  test('OTP challenge carries the masked email when the server sends one, and tolerates an older server', () {
    final withEmail = OtpChallenge.fromJson({
      'challengeToken': 't', 'expiresAtUtc': '2026-11-12T10:00:00Z', 'codeLength': 6,
      'resendAvailableInSeconds': 30, 'resendsRemaining': 3, 'maskedEmail': 'p***@gmail.com',
    });
    expect(withEmail.maskedEmail, 'p***@gmail.com');
    expect(OtpChallenge.fromJson({'challengeToken': 't'}).maskedEmail, isNull);
  });
}

void trustedDeviceModelTests() {
  test('sign-in parses an OTP challenge, and a trusted-phone outcome with no code', () {
    final otp = GuardianSignIn.fromJson({
      'challengeToken': 'abc',
      'expiresAtUtc': '2026-11-12T10:00:00Z',
      'codeLength': 6,
      'stage': 'OtpRequired',
    });
    final trusted = GuardianSignIn.fromJson({
      'stage': 'Authenticated',
      'session': {'token': 't', 'fullName': 'Asha'},
    });
    final reset = GuardianSignIn.fromJson({'stage': 'PasswordResetRequired', 'resetTicket': 'tkt'});

    expect(otp.stage, GuardianAuthStage.otpRequired);
    expect(otp.challenge!.challengeToken, 'abc');
    expect(trusted.stage, GuardianAuthStage.authenticated);
    expect(trusted.auth!.session!['token'], 't');
    expect(trusted.challenge, isNull);
    expect(reset.auth!.resetTicket, 'tkt');
    expect(reset.auth!.session, isNull);
  });

  test('verify result carries the device secret only when the server trusted the phone', () {
    expect(GuardianAuthResult.fromJson({'stage': 'Authenticated', 'deviceToken': 'secret'}).deviceToken, 'secret');
    expect(GuardianAuthResult.fromJson({'stage': 'Authenticated'}).deviceToken, isNull);
  });
}
