/**
 * 轻量级状态管理（Signal 模式）
 */

class Signal {
    constructor(initialValue) {
        this._value = initialValue;
        this._subscribers = new Set();
    }

    get value() {
        return this._value;
    }

    set value(newValue) {
        if (this._value !== newValue) {
            this._value = newValue;
            this._notify();
        }
    }

    /**
     * 订阅变化
     */
    subscribe(callback) {
        this._subscribers.add(callback);
        // 立即返回当前值
        callback(this._value);
        
        // 返回取消订阅函数
        return () => {
            this._subscribers.delete(callback);
        };
    }

    /**
     * 通知所有订阅者
     */
    _notify() {
        this._subscribers.forEach(callback => {
            try {
                callback(this._value);
            } catch (error) {
                console.error('[Signal] 订阅者执行失败:', error);
            }
        });
    }
}

/**
 * 创建 Signal
 */
function createSignal(initialValue) {
    const signal = new Signal(initialValue);
    
    // 返回 getter 和 setter
    return [
        () => signal.value,
        (newValue) => { signal.value = newValue; },
        (callback) => signal.subscribe(callback)
    ];
}

/**
 * 创建计算属性
 */
function createComputed(computeFn, dependencies) {
    const [getValue, setValue, subscribe] = createSignal(computeFn());
    
    // 监听所有依赖
    dependencies.forEach(dep => {
        if (typeof dep === 'function' && dep.length === 0) {
            // 假设这是一个 getter 函数
            // 这里简化处理，实际需要更复杂的依赖追踪
        }
    });
    
    return getValue;
}

export { Signal, createSignal, createComputed };
