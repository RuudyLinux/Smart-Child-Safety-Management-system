import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';
import 'package:provider/provider.dart';
import '../../core/api_client.dart';
import '../../core/theme.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/cg_avatar.dart';
import '../../widgets/cg_child_card.dart';
import '../../widgets/cg_states.dart';
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
      appBar: AppBar(title: const Text('Profile')),
      body: profile == null
          ? const CgLoadingState()
          : ListView(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
              children: [
                Center(
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
                                    color: CgColors.primary,
                                    shape: BoxShape.circle,
                                    border: Border.all(color: CgColors.background, width: 3),
                                  ),
                                  child: const Icon(Icons.camera_alt_rounded, color: Colors.white, size: 16),
                                ),
                              ),
                            ),
                          ),
                        ],
                      ),
                      const SizedBox(height: 14),
                      Text(profile.fullName, style: const TextStyle(fontSize: 19, fontWeight: FontWeight.w800)),
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
                const SizedBox(height: 24),

                CgSectionHeader('My children (${profile.students.length})'),
                if (profile.students.isEmpty)
                  const Padding(
                    padding: EdgeInsets.symmetric(vertical: 8),
                    child: Text('No children linked yet.', style: TextStyle(color: CgColors.textMuted)),
                  )
                else
                  ...profile.students.map((s) => Padding(
                        padding: const EdgeInsets.only(bottom: 10),
                        child: CgChildCard(
                          student: s,
                          photoUrl: s.hasPhoto ? auth.api.studentPhotoUrl(s.studentId) : null,
                          photoHeaders: auth.photoHeaders,
                          onTap: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => StudentDetailScreen(student: s))),
                        ),
                      )),

                const SizedBox(height: 12),
                CgSectionHeader('Security'),
                Card(
                  child: ListTile(
                    leading: const Icon(Icons.lock_outline_rounded, color: CgColors.primary),
                    title: const Text('Change password', style: TextStyle(fontWeight: FontWeight.w600)),
                    trailing: const Icon(Icons.chevron_right_rounded),
                    onTap: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const ChangePasswordScreen())),
                  ),
                ),

                const SizedBox(height: 12),
                CgSectionHeader('Notifications'),
                Card(
                  child: ListTile(
                    leading: const Icon(Icons.notifications_active_outlined, color: CgColors.primary),
                    title: const Text('Push notifications', style: TextStyle(fontWeight: FontWeight.w600)),
                    subtitle: const Text('Registered automatically when you sign in on this device.',
                        style: TextStyle(fontSize: 12)),
                  ),
                ),

                const SizedBox(height: 12),
                CgSectionHeader('Account'),
                Card(
                  child: ListTile(
                    leading: const Icon(Icons.logout_rounded, color: CgColors.error),
                    title: const Text('Sign out', style: TextStyle(color: CgColors.error, fontWeight: FontWeight.w600)),
                    onTap: () async {
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
                  final file = await ImagePicker().pickImage(source: ImageSource.camera, imageQuality: 85);
                  if (context.mounted) Navigator.pop(context, file);
                },
              ),
              ListTile(
                leading: const Icon(Icons.photo_library_rounded, color: CgColors.primary),
                title: const Text('Choose from gallery'),
                onTap: () async {
                  final file = await ImagePicker().pickImage(source: ImageSource.gallery, imageQuality: 85);
                  if (context.mounted) Navigator.pop(context, file);
                },
              ),
            ],
          ),
        ),
      );
}
