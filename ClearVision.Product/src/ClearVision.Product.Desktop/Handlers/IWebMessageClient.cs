namespace ClearVision.Product.Desktop.Handlers;

internal interface IWebMessageClient
{
    void SendEvent<T>(T eventData);

    void SendProgressMessage(string type, object payload);

    void NotifyInspectionResult(ClearVision.Product.Core.Entities.InspectionResult result, Guid projectId);
}
