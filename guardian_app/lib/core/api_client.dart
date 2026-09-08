import 'dart:convert';
import 'package:http/http.dart' as http;
import 'package:shared_preferences/shared_preferences.dart';

/// Base URL for ChildGuard.Api. Android emulator reaches the host machine via
/// 10.0.2.2; change to your LAN IP for a physical device, or the deployed
/// API URL for production — never hardcode a production URL here.
class ApiConfig {
  static const String baseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://10.0.2.2:5275',
  );
}

class ApiException implements Exception {
  final String message;
  final int statusCode;
  ApiException(this.message, this.statusCode);
  @override
  String toString() => message;
}

/// Thin JSON HTTP client for the Guardian app. Persists the JWT via
/// SharedPreferences and attaches it to every request.
class ApiClient {
  static const _tokenKey = 'cg_auth_token';
  String? _cachedToken;

  Future<String?> get token async {
    if (_cachedToken != null) return _cachedToken;
    final prefs = await SharedPreferences.getInstance();
    _cachedToken = prefs.getString(_tokenKey);
    return _cachedToken;
  }

  Future<void> setToken(String? token) async {
    _cachedToken = token;
    final prefs = await SharedPreferences.getInstance();
    if (token == null) {
      await prefs.remove(_tokenKey);
    } else {
      await prefs.setString(_tokenKey, token);
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

  Uri _uri(String path) => Uri.parse('${ApiConfig.baseUrl}$path');

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

  dynamic _handle(http.Response resp) {
    if (resp.statusCode >= 200 && resp.statusCode < 300) {
      if (resp.body.isEmpty) return null;
      return jsonDecode(resp.body);
    }
    String message = 'Request failed (${resp.statusCode}).';
    try {
      final decoded = jsonDecode(resp.body);
      if (decoded is Map && decoded['message'] != null) message = decoded['message'].toString();
    } catch (_) {}
    throw ApiException(message, resp.statusCode);
  }
}
