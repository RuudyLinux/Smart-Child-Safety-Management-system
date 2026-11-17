namespace ChildGuard.Application.DTOs;

/// <summary>An image on its way in. Bytes are already read; the caller's filename is not carried because it is not trusted.</summary>
public record UploadPhotoRequest(byte[] Data);

/// <summary>An image on its way out, addressed only by owner id — never by a storage path.</summary>
public record PhotoContentDto(byte[] Data, string ContentType, DateTime UpdatedAt);

/// <summary>What a client needs to decide between rendering a photo and rendering initials.</summary>
public record PhotoStateDto(bool HasPhoto, DateTime? UpdatedAt, int? ByteSize, string? ContentType);
