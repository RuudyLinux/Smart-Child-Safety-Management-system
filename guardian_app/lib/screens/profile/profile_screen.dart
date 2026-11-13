import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';
import 'package:provider/provider.dart';
import '../../core/api_client.dart';
import '../../core/theme.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_avatar.dart';
import '../../widgets/cg_glass.dart';
import '../../widgets/cg_states.dart';
import '../../widgets/cg_ui.dart';
import '../../widgets/photo_capture.dart';
import '../face/face_registration_screen.dart';
import '../notifications/notifications_screen.dart';
import '../auth/login_screen.dart';
import '../home/student_detail_screen.dart';
import 'change_password_screen.dart';

class ProfileScreen extends StatefulWidget {
  /// True when shown as a bottom-nav tab rather than pushed from elsewhere.
  /// Reserved for layout differences between the two contexts.
  final bool embedded;
  const ProfileScreen({this.embedded = false, super.key});

  @override
  State<ProfileScreen> createState() => _ProfileScreenState();
}

class _ProfileScreenState extends State<ProfileScreen> {
  bool _uploadingPhoto = false;

  Future<void> _changePhoto() async {
    final picked = await showModalBottomSheet<XFile?>(
      context: context,
      backgroundColor: Colors.transparent,
      builder: (sheetContext) => _PhotoSourceSheet(),
    );
    if (picked == null || !mounted) return;

    setState(() => _uploadingPhoto = true);
    try {
      final auth = context.read<AuthProvider>();
      final bytes = await picked.readAsBytes();
      await auth.api.uploadMyPhoto(bytes, picked.name, picked.mimeType ?? 'image/jpeg');
      await auth.refreshProfile();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Photo updated.')));
      }
    } on ApiException catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Could not upload the photo. Check your connection.')));
      }
    } finally {
      if (mounted) setState(() => _uploadingPhoto = false);
    }
  }

  Future<void> _forgetTrustedPhones() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Ask for the code again?'),
        content: const Text(
            'Every phone you have signed in on, including this one, will need the emailed code at its next sign-in. '
            'Do this if a phone was lost or shared.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(dialogContext, false), child: const Text('Cancel')),
          TextButton(onPressed: () => Navigator.pop(dialogContext, true), child: const Text('Yes, ask again')),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;
    try {
      await context.read<AuthProvider>().api.forgetTrustedDevices();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Done. The next sign-in on each phone needs the emailed code.')));
      }
    } on ApiException catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    } catch (_) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Could not reach the server. Try again.')));
    }
  }

  Future<void> _removePhoto() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Remove photo?'),
        content: const Text('You can add a new one at any time.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(dialogContext, false), child: const Text('Cancel')),
          TextButton(onPressed: () => Navigator.pop(dialogContext, true), child: const Text('Remove')),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;

    setState(() => _uploadingPhoto = true);
    try {
      final auth = context.read<AuthProvider>();
      await auth.api.deleteMyPhoto();
      await auth.refreshProfile();
    } catch (_) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Could not remove the photo.')));
    } finally {
      if (mounted) setState(() => _uploadingPhoto = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthProvider>();
    final profile = auth.profile;

    return Scaffold(
      appBar: widget.embedded ? null : AppBar(title: const Text('Profile')),
      body: profile == null
          ? const CgLoadingState()
          : ListView(
              padding: EdgeInsets.fromLTRB(16, widget.embedded ? MediaQuery.paddingOf(context).top + 8 : 8, 16, 110),
              children: [
                if (widget.embedded) const CgPageHead(title: 'Profile', subtitle: 'Your account, photo and security.'),
                CgFadeIn(
                  child: CgGlass(
                    radius: 26,
                    padding: const EdgeInsets.fromLTRB(16, 24, 16, 20),
                    shadow: CgShadows.md,
                    gradient: const LinearGradient(
                      begin: Alignment.topLeft,
                      end: Alignment.bottomRight,
                      colors: [Color(0xF2DCEBFF), Color(0xD9FFFFFF), Color(0xF2EFEDFD)],
                    ),
                    child: Center(
                    child: Column(
                      children: [
                        Stack(
                          clipBehavior: Clip.none,
                          children: [
                            Opacity(
                              opacity: _uploadingPhoto ? 0.5 : 1,
                              child: CgAvatar(
                                name: profile.fullName,
                                photoUrl: profile.hasPhoto ? auth.api.myPhotoUrl : null,
                                headers: auth.photoHeaders,
                                size: 92,
                              ),
                            ),
                            if (_uploadingPhoto)
                              const Positioned.fill(child: Center(child: CircularProgressIndicator(strokeWidth: 2.4))),
                            Positioned(
                              right: -2,
                              bottom: -2,
                              child: Semantics(
                                label: 'Change photo',
                                button: true,
                                child: GestureDetector(
                                  onTap: _uploadingPhoto ? null : _changePhoto,
                                  child: Container(
                                    width: 32,
                                    height: 32,
                                    decoration: BoxDecoration(
                                      gradient: CgColors.brandGradient,
                                      shape: BoxShape.circle,
                                      border: Border.all(color: Colors.white, width: 3),
                                      boxShadow: CgShadows.glow(),
                                    ),
                                    child: const Icon(Icons.camera_alt_rounded, color: Colors.white, size: 16),
                                  ),
                                ),
                              ),
                            ),
                          ],
                        ),
                        const SizedBox(height: 14),
                        Text(profile.fullName, style: const TextStyle(fontSize: 21, fontWeight: FontWeight.w800, letterSpacing: -0.3)),
                        const SizedBox(height: 3),
                        Text(profile.email, style: const TextStyle(color: CgColors.textMuted, fontSize: 13)),
                        if (profile.phone != null) Text(profile.phone!, style: const TextStyle(color: CgColors.textMuted, fontSize: 13)),
                        const SizedBox(height: 10),
                        Wrap(
                          spacing: 8,
                          children: [
                            profile.faceRegistered
                                ? CgBadge.success('Face registered', icon: Icons.face_retouching_natural_rounded)
                                : CgBadge.warning('Face not registered', icon: Icons.warning_amber_rounded),
                            if (profile.hasPhoto)
                              TextButton(onPressed: _uploadingPhoto ? null : _removePhoto, child: const Text('Remove photo')),
                          ],
                        ),
                      ],
                    ),
                  ),
                  ),
                ),
                const SizedBox(height: 18),
                CgCard(
                  title: 'Account details',
                  foot: Row(children: [
                    TextButton.icon(
                      onPressed: _uploadingPhoto ? null : _changePhoto,
                      icon: const Icon(Icons.photo_camera_outlined, size: 18),
                      label: Text(profile.hasPhoto ? 'Change photo' : 'Add photo'),
                    ),
                    if (profile.hasPhoto)
                      TextButton.icon(
                        onPressed: _uploadingPhoto ? null : _removePhoto,
                        icon: const Icon(Icons.delete_outline_rounded, size: 18),
                        label: const Text('Remove'),
                        style: TextButton.styleFrom(foregroundColor: CgColors.errorText),
                      ),
                  ]),
                  child: CgDefList([
                    CgDefList.text('Full name', profile.fullName),
                    CgDefList.text('Email', profile.email),
                    CgDefList.text('Contact number', profile.phone),
                    ('Profile photo', profile.hasPhoto ? CgBadge.success('Uploaded') : CgBadge.neutral('Not set')),
                  ]),
                ),
                CgCard(
                  title: 'Pickup verification',
                  subtitle: 'Your face is checked at the school desk before a handover',
                  padding: const EdgeInsets.fromLTRB(16, 2, 12, 2),
                  child: CgRow(
                    divider: false,
                    leading: CgIconTile(
                      icon: Icons.face_retouching_natural_rounded,
                      color: profile.faceRegistered ? CgColors.successText : CgColors.warning,
                      background: profile.faceRegistered ? CgColors.successSoft : CgColors.warningSoft,
                      size: 38,
                    ),
                    title: profile.faceRegistered ? 'Face photos' : 'Register your face',
                    subtitle: profile.faceRegistered ? 'Add or remove reference photos' : 'Required before you can collect a child',
                    onTap: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const FaceRegistrationScreen())),
                  ),
                ),
                CgCard(
                  title: 'My children',
                  subtitle: '${profile.students.length} linked',
                  padding: const EdgeInsets.fromLTRB(16, 2, 12, 2),
                  child: profile.students.isEmpty
                      ? const Padding(
                          padding: EdgeInsets.symmetric(vertical: 12),
                          child: Text('No children linked yet.', style: TextStyle(color: CgColors.textMuted)),
                        )
                      : Column(children: [
                          for (final (i, s) in profile.students.indexed)
                            CgRow(
                              divider: i != profile.students.length - 1,
                              leading: CgAvatar(
                                name: s.fullName,
                                photoUrl: s.hasPhoto ? auth.api.studentPhotoUrl(s.studentId) : null,
                                headers: auth.photoHeaders,
                                size: 38,
                                square: true,
                              ),
                              title: s.fullName,
                              subtitle: [s.grade ?? 'No grade', s.relationship ?? 'Guardian'].join(' · '),
                              trailing: CgBadge.safety(s.safetyStatus),
                              onTap: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => StudentDetailScreen(student: s))),
                            ),
                        ]),
                ),
                CgCard(
                  title: 'Security & notifications',
                  padding: const EdgeInsets.fromLTRB(16, 2, 12, 2),
                  child: Column(children: [
                    CgRow(
                      leading: const CgIconTile(icon: Icons.lock_outline_rounded, color: CgColors.primaryDeep, background: CgColors.primarySoft, size: 38),
                      title: 'Change password',
                      onTap: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const ChangePasswordScreen())),
                    ),
                    CgRow(
                      leading: const CgIconTile(icon: Icons.phonelink_lock_rounded, color: CgColors.primaryDeep, background: CgColors.primarySoft, size: 38),
                      title: 'Trusted phones',
                      subtitle: 'You enter the emailed code once on each phone. Tap to make every phone ask for it again.',
                      onTap: _forgetTrustedPhones,
                    ),
                    CgRow(
                      leading: const CgIconTile(icon: Icons.notifications_active_outlined, color: CgColors.infoText, background: CgColors.infoSoft, size: 38),
                      title: 'Alerts',
                      subtitle: 'Push notifications are registered when you sign in on this device',
                      divider: false,
                      onTap: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const NotificationsScreen())),
                    ),
                  ]),
                ),
                SizedBox(
                  width: double.infinity,
                  child: OutlinedButton.icon(
                    style: OutlinedButton.styleFrom(foregroundColor: CgColors.errorText, side: const BorderSide(color: CgColors.errorBorder)),
                    icon: const Icon(Icons.logout_rounded),
                    label: const Text('Sign out'),
                    onPressed: () async {
                      await context.read<AuthProvider>().logout();
                      if (context.mounted) {
                        Navigator.of(context).pushAndRemoveUntil(MaterialPageRoute(builder: (_) => const LoginScreen()), (route) => false);
                      }
                    },
                  ),
                ),
              ],
            ),
    );
  }
}

class _PhotoSourceSheet extends StatelessWidget {
  @override
  Widget build(BuildContext context) => SafeArea(
        child: Container(
          decoration: const BoxDecoration(
            color: CgColors.card,
            borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
          ),
          padding: const EdgeInsets.fromLTRB(20, 12, 20, 20),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Container(width: 36, height: 4, decoration: BoxDecoration(color: CgColors.border, borderRadius: BorderRadius.circular(2))),
              const SizedBox(height: 18),
              const Text('Update photo', style: TextStyle(fontWeight: FontWeight.w800, fontSize: 16)),
              const SizedBox(height: 14),
              ListTile(
                leading: const Icon(Icons.camera_alt_rounded, color: CgColors.primary),
                title: const Text('Take a photo'),
                onTap: () async {
                  final file = await pickPhoto(context, ImageSource.camera, preferredCamera: CameraDevice.front);
                  if (context.mounted) Navigator.pop(context, file);
                },
              ),
              ListTile(
                leading: const Icon(Icons.photo_library_rounded, color: CgColors.primary),
                title: const Text('Choose from gallery'),
                onTap: () async {
                  final file = await pickPhoto(context, ImageSource.gallery);
                  if (context.mounted) Navigator.pop(context, file);
                },
              ),
            ],
          ),
        ),
      );
}
