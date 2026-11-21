namespace Bepthaymao.Application.Abstractions.Errors;

/// <summary>Nội dung tệp không phải định dạng được nhận; thông điệp tiếng Việt hiển thị cho Owner, map sang 415.</summary>
public sealed class UnsupportedMediaTypeException(string message) : Exception(message);
