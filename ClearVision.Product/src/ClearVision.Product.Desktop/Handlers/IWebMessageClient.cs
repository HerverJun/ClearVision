namespace ClearVision.Product.Desktop.Handlers;

internal interface IWebMessageClient
{
    void SendEvent<T>(T eventData);

    void SendProgressMessage(string type, object payload);

    void SendBoundEvent<T>(T eventData, WebMessageDeliveryBinding binding);

    void SendBoundProgressMessage(string type, object payload, WebMessageDeliveryBinding binding);

    void NotifyInspectionResult(ClearVision.Product.Core.Entities.InspectionResult result, Guid projectId);
}
