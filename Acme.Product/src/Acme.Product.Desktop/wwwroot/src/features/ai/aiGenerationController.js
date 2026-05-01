/**
 * @typedef {Object} AiGenerationControllerOptions
 * @property {{ emit?: Function }} eventBus
 * @property {{ get?: Function }} serviceRegistry
 * @property {() => Promise<any>} ensureAiPanel
 * @property {(view: string) => Promise<void>} switchView
 * @property {(view: string) => void} setCurrentView
 * @property {(view: string) => void} syncActiveNavButton
 */

/**
 * Coordinates AI panel navigation and publish-only AI lifecycle events.
 *
 * @param {AiGenerationControllerOptions} options
 */
export function createAiGenerationController(options) {
    const {
        eventBus,
        serviceRegistry,
        ensureAiPanel,
        switchView,
        setCurrentView,
        syncActiveNavButton
    } = options;

    async function open() {
        setCurrentView('ai');
        syncActiveNavButton('ai');
        await switchView('ai');
        return ensureAiPanel();
    }

    function publishApplied(flow) {
        eventBus?.emit?.('ai:applied', { flow });

        const flowCanvasAdapter = serviceRegistry?.get?.('flowCanvasAdapter');
        if (flowCanvasAdapter?.getRevision) {
            eventBus?.emit?.('flow:changed', {
                source: 'ai',
                revision: flowCanvasAdapter.getRevision()
            });
        }
    }

    return {
        open,
        publishApplied
    };
}
