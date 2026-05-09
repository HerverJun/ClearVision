namespace Acme.Product.Desktop.Handlers;

internal interface IWebMessageClient
{
    void SendEvent<T>(T eventData);

    void SendProgressMessage(string type, object payload);

    void NotifyInspectionResult(Acme.Product.Core.Entities.InspectionResult result, Guid projectId);
}
