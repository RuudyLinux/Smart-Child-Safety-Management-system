import 'dart:convert';
import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;
import 'package:http_parser/http_parser.dart';
import 'dart:io' show Platform;

/// Base URL for ChildGuard.Api. Always pass --dart-define=API_BASE_URL=...
/// for a physical device or a deployed API. Without one, the default is
/// chosen per platform rather than a single Android-only address — running
/// on web, Windows, macOS or Linux with no override previously sent every
/// request to 10.0.2.2 (meaningless outside an Android emulator) and failed
/// silently as "could not reach server", which looked exactly like a broken
/// login. Only the Android emulator needs the 10.0.2.2 host-loopback alias;
/// every other target reaches the API directly via localhost.
class ApiConfig {
  static const String _override = String.fromEnvironment('API_BASE_URL');

  static String get baseUrl {
    if (_override.isNotEmpty) return _override;
    if (kIsWeb) return 'http://localhost:5275';
    try {
      if (Platform.isAndroid) return 'http://10.0.2.2:5275';
    } catch (_) {
      // Platform.* throws on web, but kIsWeb already returned above.
    }
    return 'http://localhost:5275';
  }
}

class ApiException implements Exception {
  final String message;
  final int statusCode;
  ApiException(this.message, this.statusCode);
  @override
  String toString() => message;
}

/// Thin JSON HTTP client for the Guardian app. Persists the JWT in the
/// platform secure store (Keychain on iOS, Keystore-backed on Android) and
/// attaches it to every request.
class ApiClient {
  static const _tokenKey = 'cg_auth_token';
  static const _storage = FlutterSecureStorage();
  String? _cachedToken;

  /// Called whenever a request comes back 401 — the token is already cleared
  /// by the time this runs, so the listener (AuthProvider) only needs to
  /// reset its own state and get the guardian back to the login screen.
  void Function()? onUnauthorized;

  Future<String?> get token async {
    if (_cachedToken != null) return _cachedToken;
    _cachedToken = await _storage.read(key: _tokenKey);
    return _cachedToken;
  }

  Future<void> setToken(String? token) async {
    _cachedToken = token;
    if (token == null) {
      await _storage.delete(key: _tokenKey);
    } else {
      await _storage.write(key: _tokenKey, value: token);
    }
  }

  Future<Map<String, String>> _headers({bool auth = true}) async {
    final headers = {'Content-Type': 'application/json'};
    if (auth) {
      final t = await token;
      if (t != null) headers['Authorization'] = 'Bearer $t';
    }
    return headers;
  }

  /// Headers for a request that carries no JSON body (image fetch, multipart
  /// upload) — just the bearer token, so the photo endpoints stay behind the
  /// same authorization as the rest of the API.
  Future<Map<String, String>> authHeaders() async {
    final t = await token;
    return t == null ? {} : {'Authorization': 'Bearer $t'};
  }

  /// Synchronous version of [authHeaders], for `Image.network(headers: ...)`
  /// which needs a plain Map rather than a Future. Safe once [token] has been
  /// read at least once (bootstrap does this), since the value is cached.
  Map<String, String> get cachedAuthHeaders =>
      _cachedToken == null ? const {} : {'Authorization': 'Bearer $_cachedToken'};

  Uri _uri(String path) => Uri.parse('${ApiConfig.baseUrl}$path');

  /// Full URL for an authenticated image endpoint, for use with
  /// `Image.network(url, headers: await client.authHeaders())`.
  String imageUrl(String path) => _uri(path).toString();

  /// Uploads a file as multipart/form-data under the field name "file",
  /// matching PhotoUploads.ReadAsync on the API. Used for guardian and
  /// student profile photo uploads.
  Future<dynamic> postFile(String path, List<int> bytes, String filename, String contentType) async {
    final request = http.MultipartRequest('POST', _uri(path));
    request.headers.addAll(await authHeaders());
    request.files.add(http.MultipartFile.fromBytes(
      'file', bytes,
      filename: filename,
      contentType: MediaType.parse(contentType),
    ));
    final streamed = await request.send();
    final resp = await http.Response.fromStream(streamed);
    return _handle(resp);
  }

  Future<dynamic> delete(String path) async {
    final resp = await http.delete(_uri(path), headers: await _headers(auth: false)..addAll(await authHeaders()));
    return _handle(resp);
  }

  Future<dynamic> get(String path) async {
    final resp = await http.get(_uri(path), headers: await _headers());
    return _handle(resp);
  }

  Future<dynamic> post(String path, Map<String, dynamic> body, {bool auth = true}) async {
    final resp = await http.post(_uri(path), headers: await _headers(auth: auth), body: jsonEncode(body));
    return _handle(resp);
  }

  Future<dynamic> put(String path, Map<String, dynamic> body) async {
    final resp = await http.put(_uri(path), headers: await _headers(), body: jsonEncode(body));
    return _handle(resp);
  }

  Future<dynamic> patch(String path, Map<String, dynamic> body) async {
    final resp = await http.patch(_uri(path), headers: await _headers(), body: jsonEncode(body));
    return _handle(resp);
  }

  Future<dynamic> _handle(http.Response resp) async {
    if (resp.statusCode >= 200 && resp.statusCode < 300) {
      if (resp.body.isEmpty) return null;
      return jsonDecode(resp.body);
    }
    String message = 'Request failed (${resp.statusCode}).';
    try {
      final decoded = jsonDecode(resp.body);
      if (decoded is Map && decoded['message'] != null) message = decoded['message'].toString();
    } catch (_) {}

    // The token is no longer valid for any reason (expired, account
    // deactivated) — drop it and let the app route back to login instead of
    // every screen retrying a request that will keep failing the same way.
    if (resp.statusCode == 401) {
      await setToken(null);
      onUnauthorized?.call();
    }

    throw ApiException(message, resp.statusCode);
  }
}
