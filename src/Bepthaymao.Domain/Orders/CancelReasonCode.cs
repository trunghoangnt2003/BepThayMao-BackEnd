namespace Bepthaymao.Domain.Orders;

/// <summary>Mã lý do hủy đơn bởi nhân viên (D-14, D-20).</summary>
public enum CancelReasonCode
{
    OutOfStock,
    CustomerRequested,
    CustomerUnreachable,
    OutOfDeliveryArea,
    Other,
}

public static class CancelReasonCodes
{
    public static string Label(CancelReasonCode code) => code switch
    {
        CancelReasonCode.OutOfStock => "Hết món",
        CancelReasonCode.CustomerRequested => "Khách yêu cầu hủy",
        CancelReasonCode.CustomerUnreachable => "Không liên lạc được khách",
        CancelReasonCode.OutOfDeliveryArea => "Ngoài vùng giao",
        CancelReasonCode.Other => "Khác",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };
}
