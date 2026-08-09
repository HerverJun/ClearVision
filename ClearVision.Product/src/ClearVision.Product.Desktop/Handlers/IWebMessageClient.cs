namespace ClearVision.Product.Desktop.Handlers;

internal interface IHostMessageClient
{
    void SendEvent<T>(T eventData);
}

internal interface IWebMessageClient : IHostMessageClient
{

    void SendProgressMessage(string type, object payload);

    void NotifyInspectionResult(ClearVision.Product.Core.Entities.InspectionResult result, Guid projectId);
}
