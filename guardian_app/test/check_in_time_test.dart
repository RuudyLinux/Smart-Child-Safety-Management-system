// The API sends attendance time as a .NET TimeOnly ("16:50:03.0878150"). Parents
// must see a clock time, not the raw value.
import 'package:flutter_test/flutter_test.dart';
import 'package:guardian_app/models/models.dart';

void main() {
  test('formats API TimeOnly values as a short clock time', () {
    expect(formatCheckInTime('16:50:03.0878150'), '4:50 PM');
    expect(formatCheckInTime('08:05:00'), '8:05 AM');
    expect(formatCheckInTime('00:00'), '12:00 AM');
  });

  test('leaves unexpected values alone instead of failing', () {
    expect(formatCheckInTime('not a time'), 'not a time');
    expect(formatCheckInTime(null), isNull);
  });
}
