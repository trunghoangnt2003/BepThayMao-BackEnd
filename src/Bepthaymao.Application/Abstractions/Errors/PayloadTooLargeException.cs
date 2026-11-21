namespace Bepthaymao.Application.Abstractions.Errors;

/// <summary>Tệp tải lên vượt giới hạn (cỡ tệp hoặc số điểm ảnh); thông điệp tiếng Việt hiển thị cho Owner, map sang 413.</summary>
public sealed class PayloadTooLargeException(string message) : Exception(message);
