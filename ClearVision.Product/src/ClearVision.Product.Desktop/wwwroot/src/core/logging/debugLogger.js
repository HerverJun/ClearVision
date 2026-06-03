const DEBUG_FLAGS = [
    '__CLEARVISION_DEBUG__',
    '__FLOW_CANVAS_DEBUG__'
];

const originalConsole = {
    debug: console.debug?.bind(console) || console.log.bind(console),
    info: console.info?.bind(console) || console.log.bind(console),
    log: console.log.bind(console),
    warn: console.warn?.bind(console) || console.log.bind(console)
};

let consoleGateInstalled = false;

function isDebugEnabled() {
    if (typeof window === 'undefined') {
        return false;
    }

    return DEBUG_FLAGS.some(flag => window[flag] === true);
}

function write(method, args) {
    if (!isDebugEnabled()) {
        return;
    }

    const target = originalConsole[method] || originalConsole.log;
    target(...args);
}

export function installConsoleGate() {
    if (consoleGateInstalled || typeof window === 'undefined') {
        return;
    }

    consoleGateInstalled = true;

    console.debug = (...args) => write('debug', args);
    console.log = (...args) => write('log', args);
    console.info = (...args) => write('info', args);
    console.warn = (...args) => write('warn', args);
}

const debugLogger = {
    debug(...args) {
        write('debug', args);
    },
    info(...args) {
        write('info', args);
    },
    warn(...args) {
        write('warn', args);
    }
};

export { isDebugEnabled };
export default debugLogger;
