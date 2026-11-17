import 'package:camera/camera.dart';
import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';
import '../core/theme.dart';

/// One place for "take a photo" / "choose from gallery".
///
/// On a phone the image_picker plugin opens the real camera or gallery. In a web browser its camera mode is
/// only a file input with a `capture` hint, which desktop browsers ignore, so "Take a photo" opened the file
/// chooser exactly like "Choose from gallery". On web the camera source therefore opens a live webcam preview
/// ([WebCameraScreen]) instead.
Future<XFile?> pickPhoto(
  BuildContext context,
  ImageSource source, {
  int maxSize = 1024,
  int quality = 85,
  CameraDevice preferredCamera = CameraDevice.rear,
}) {
  if (kIsWeb && source == ImageSource.camera) {
    return Navigator.of(context).push<XFile?>(
      MaterialPageRoute(
        fullscreenDialog: true,
        builder: (_) => WebCameraScreen(front: preferredCamera == CameraDevice.front),
      ),
    );
  }
  return ImagePicker().pickImage(
    source: source,
    preferredCameraDevice: preferredCamera,
    imageQuality: quality,
    maxWidth: maxSize.toDouble(),
    maxHeight: maxSize.toDouble(),
  );
}

/// Live webcam preview with a capture button. Pops the captured [XFile], or null when cancelled.
/// If no camera is available (or permission is denied) it says so and offers the file chooser instead.
class WebCameraScreen extends StatefulWidget {
  final bool front;
  const WebCameraScreen({this.front = true, super.key});

  @override
  State<WebCameraScreen> createState() => _WebCameraScreenState();
}

class _WebCameraScreenState extends State<WebCameraScreen> {
  CameraController? _controller;
  String? _error;
  bool _capturing = false;

  @override
  void initState() {
    super.initState();
    _start();
  }

  Future<void> _start() async {
    try {
      final cameras = await availableCameras();
      if (cameras.isEmpty) {
        if (mounted) setState(() => _error = 'No camera was found on this device.');
        return;
      }
      final wanted = widget.front ? CameraLensDirection.front : CameraLensDirection.back;
      final chosen = cameras.firstWhere((c) => c.lensDirection == wanted, orElse: () => cameras.first);
      final controller = CameraController(chosen, ResolutionPreset.high, enableAudio: false);
      await controller.initialize();
      if (!mounted) {
        await controller.dispose();
        return;
      }
      setState(() => _controller = controller);
    } on CameraException catch (e) {
      final denied = e.code == 'CameraAccessDenied' || e.code == 'CameraAccessDeniedWithoutPrompt';
      if (mounted) {
        setState(() => _error = denied
            ? 'Camera access was blocked. Allow the camera for this site in the browser address bar, then try again.'
            : 'The camera could not be started (${e.code}).');
      }
    } catch (_) {
      if (mounted) setState(() => _error = 'The camera could not be started.');
    }
  }

  Future<void> _capture() async {
    final controller = _controller;
    if (controller == null || _capturing) return;
    setState(() => _capturing = true);
    try {
      final file = await controller.takePicture();
      if (mounted) Navigator.pop(context, file);
    } catch (_) {
      if (mounted) {
        setState(() => _capturing = false);
        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Could not take the photo. Try again.')));
      }
    }
  }

  Future<void> _chooseFile() async {
    final file = await ImagePicker().pickImage(source: ImageSource.gallery, imageQuality: 85, maxWidth: 1024, maxHeight: 1024);
    if (mounted && file != null) Navigator.pop(context, file);
  }

  @override
  void dispose() {
    _controller?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final controller = _controller;
    return Scaffold(
      backgroundColor: Colors.black,
      appBar: AppBar(
        backgroundColor: Colors.black,
        foregroundColor: Colors.white,
        title: const Text('Take a photo'),
        leading: IconButton(icon: const Icon(Icons.close_rounded), onPressed: () => Navigator.pop(context)),
      ),
      body: _error != null
          ? Center(
              child: Padding(
                padding: const EdgeInsets.all(28),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    const Icon(Icons.no_photography_rounded, size: 48, color: Colors.white70),
                    const SizedBox(height: 14),
                    Text(_error!, textAlign: TextAlign.center, style: const TextStyle(color: Colors.white, fontSize: 14, height: 1.5)),
                    const SizedBox(height: 18),
                    ElevatedButton.icon(onPressed: _chooseFile, icon: const Icon(Icons.photo_library_rounded), label: const Text('Choose a file instead')),
                  ],
                ),
              ),
            )
          : controller == null
              ? const Center(child: CircularProgressIndicator())
              : Column(
                  children: [
                    Expanded(child: Center(child: CameraPreview(controller))),
                    SafeArea(
                      top: false,
                      child: Padding(
                        padding: const EdgeInsets.symmetric(vertical: 20),
                        child: GestureDetector(
                          onTap: _capturing ? null : _capture,
                          child: Container(
                            width: 72,
                            height: 72,
                            decoration: BoxDecoration(
                              shape: BoxShape.circle,
                              color: _capturing ? Colors.white38 : Colors.white,
                              border: Border.all(color: CgColors.primary, width: 4),
                            ),
                            child: const Icon(Icons.camera_alt_rounded, color: CgColors.primaryDeep, size: 30),
                          ),
                        ),
                      ),
                    ),
                  ],
                ),
    );
  }
}
