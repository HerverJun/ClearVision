/**
 * Documents and centralizes frontend communication boundaries.
 */
const CommunicationChannel = Object.freeze({
    HTTP: 'http',
    WEBVIEW: 'webview',
    SSE: 'sse',
    EVENT_BUS: 'event-bus'
});

const CommunicationUseCase = Object.freeze({
    CRUD_QUERY: 'crud-query',
    COMMAND: 'command',
    HOST_COMMAND: 'host-command',
    REALTIME_EVENT: 'realtime-event',
    INTERNAL_EVENT: 'internal-event'
});

function resolveCommunicationChannel(useCase) {
    switch (useCase) {
        case CommunicationUseCase.CRUD_QUERY:
        case CommunicationUseCase.COMMAND:
            return CommunicationChannel.HTTP;
        case CommunicationUseCase.HOST_COMMAND:
            return CommunicationChannel.WEBVIEW;
        case CommunicationUseCase.REALTIME_EVENT:
            return CommunicationChannel.SSE;
        case CommunicationUseCase.INTERNAL_EVENT:
            return CommunicationChannel.EVENT_BUS;
        default:
            return CommunicationChannel.HTTP;
    }
}

export {
    CommunicationChannel,
    CommunicationUseCase,
    resolveCommunicationChannel
};
