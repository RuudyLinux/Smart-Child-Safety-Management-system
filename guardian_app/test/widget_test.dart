// Basic smoke test: the app boots to the splash screen without throwing.
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:guardian_app/main.dart';

void main() {
  testWidgets('App boots to splash screen', (WidgetTester tester) async {
    await tester.pumpWidget(const ChildGuardApp());
    // The splash screen shows the app name and a loading indicator before
    // it decides (via API) whether to route to Login or Home.
    expect(find.byType(CircularProgressIndicator), findsOneWidget);
  });
}
