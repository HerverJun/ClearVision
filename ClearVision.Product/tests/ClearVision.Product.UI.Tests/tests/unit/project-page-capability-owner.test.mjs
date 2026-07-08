import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import ProjectPageCapabilityOwner from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/project/projectPageCapabilityOwner.mjs';

function readOwnerSource() {
  return readFileSync(
    new URL('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/project/projectPageCapabilityOwner.mjs', import.meta.url),
    'utf8'
  );
}

class FakeClassList {
  constructor(element) {
    this.element = element;
    this.tokens = new Set();
  }

  add(...tokens) {
    for (const token of tokens) {
      if (token) {
        this.tokens.add(token);
      }
    }
    this.sync();
  }

  remove(...tokens) {
    for (const token of tokens) {
      this.tokens.delete(token);
    }
    this.sync();
  }

  contains(token) {
    return this.tokens.has(token);
  }

  setFromString(value) {
    this.tokens = new Set(String(value || '').split(/\s+/).filter(Boolean));
    this.sync();
  }

  sync() {
    this.element._className = Array.from(this.tokens).join(' ');
  }
}

class FakeElement {
  constructor(tagName = 'div') {
    this.tagName = String(tagName).toUpperCase();
    this.children = [];
    this.parentNode = null;
    this.dataset = {};
    this.attributes = new Map();
    this.listeners = new Map();
    this.classList = new FakeClassList(this);
    this.style = {};
    this.hidden = false;
    this.disabled = false;
    this.value = '';
    this.id = '';
    this.type = '';
    this._className = '';
    this._textContent = '';
    this.innerHTML = '';
  }

  get className() {
    return this._className;
  }

  set className(value) {
    this.classList.setFromString(value);
  }

  get textContent() {
    return this._textContent;
  }

  set textContent(value) {
    this._textContent = String(value ?? '');
  }

  appendChild(child) {
    child.parentNode = this;
    this.children.push(child);
    return child;
  }

  removeChild(child) {
    const index = this.children.indexOf(child);
    if (index >= 0) {
      this.children.splice(index, 1);
      child.parentNode = null;
    }
    return child;
  }

  addEventListener(type, listener) {
    const listeners = this.listeners.get(type) || [];
    listeners.push(listener);
    this.listeners.set(type, listeners);
  }

  removeEventListener(type, listener) {
    const listeners = this.listeners.get(type) || [];
    this.listeners.set(type, listeners.filter(item => item !== listener));
  }

  async dispatchEvent(event) {
    const normalized = {
      target: this,
      preventDefault() {},
      ...event,
      type: event?.type || 'event'
    };
    const listeners = this.listeners.get(normalized.type) || [];
    for (const listener of listeners) {
      await listener(normalized);
    }
  }

  async click() {
    if (this.disabled) {
      return;
    }

    await this.dispatchEvent({ type: 'click' });
  }

  focus() {
    this.focused = true;
  }

  setAttribute(name, value = '') {
    this.attributes.set(name, String(value));
    if (name === 'id') {
      this.id = String(value);
    } else if (name === 'class') {
      this.className = String(value);
    } else if (name.startsWith('data-')) {
      this.dataset[toDatasetKey(name.slice(5))] = String(value);
    }
  }

  getAttribute(name) {
    return this.attributes.get(name) ?? null;
  }

  removeAttribute(name) {
    this.attributes.delete(name);
    if (name.startsWith('data-')) {
      delete this.dataset[toDatasetKey(name.slice(5))];
    }
  }

  querySelector(selector) {
    return findFirst(this, selector);
  }

  querySelectorAll(selector) {
    const matches = [];
    visit(this, element => {
      if (element !== this && element.matches(selector)) {
        matches.push(element);
      }
    });
    return matches;
  }

  closest(selector) {
    let current = this;
    while (current) {
      if (current.matches(selector)) {
        return current;
      }
      current = current.parentNode;
    }
    return null;
  }

  matches(selector) {
    if (selector.startsWith('.')) {
      return this.classList.contains(selector.slice(1));
    }

    const dataMatch = selector.match(/^\[data-([a-z0-9-]+)(?:="([^"]*)")?\]$/i);
    if (dataMatch) {
      const key = toDatasetKey(dataMatch[1]);
      const expected = dataMatch[2];
      if (!(key in this.dataset)) {
        return false;
      }
      return expected === undefined || String(this.dataset[key]) === expected;
    }

    return this.tagName.toLowerCase() === selector.toLowerCase();
  }
}

function toDatasetKey(name) {
  return String(name).replace(/-([a-z])/g, (_, letter) => letter.toUpperCase());
}

function visit(root, callback) {
  for (const child of root.children || []) {
    callback(child);
    visit(child, callback);
  }
}

function findFirst(root, selector) {
  let found = null;
  visit(root, element => {
    if (!found && element.matches(selector)) {
      found = element;
    }
  });
  return found;
}

function setGlobal(t, name, value) {
  const hadOwn = Object.prototype.hasOwnProperty.call(globalThis, name);
  const original = globalThis[name];
  globalThis[name] = value;
  t.after(() => {
    if (hadOwn) {
      globalThis[name] = original;
    } else {
      delete globalThis[name];
    }
  });
}

function installDom(t) {
  const body = new FakeElement('body');
  const document = {
    body,
    createElement(tagName) {
      return new FakeElement(tagName);
    },
    getElementById() {
      return null;
    },
    addEventListener() {},
    removeEventListener() {}
  };

  const failPrompt = () => {
    throw new Error('globalThis.prompt must not be used');
  };
  const failConfirm = () => {
    throw new Error('globalThis.confirm must not be used');
  };

  setGlobal(t, 'document', document);
  setGlobal(t, 'HTMLElement', FakeElement);
  setGlobal(t, 'prompt', failPrompt);
  setGlobal(t, 'confirm', failConfirm);
  setGlobal(t, 'window', {
    prompt: failPrompt,
    confirm: failConfirm
  });

  return { body };
}

function createAction(action, parent = null) {
  const element = new FakeElement('button');
  element.dataset.projectAction = action;
  if (parent) {
    parent.appendChild(element);
  }
  return element;
}

function createHarness(t, initialProjects = []) {
  installDom(t);

  let projects = [...initialProjects];
  const modals = [];
  const toasts = [];
  const adapter = {
    createProjectCalls: [],
    deleteProjectCalls: [],
    listProjectCalls: [],
    async listProjects(options) {
      this.listProjectCalls.push(options);
      return projects;
    },
    async createProject(name, description = '') {
      this.createProjectCalls.push([name, description]);
      const project = {
        id: `project-${this.createProjectCalls.length}`,
        name,
        description,
        createdAt: '2026-07-08T00:00:00.000Z',
        modifiedAt: '2026-07-08T00:00:00.000Z'
      };
      projects = [project, ...projects];
      return project;
    },
    async deleteProject(projectId) {
      this.deleteProjectCalls.push(projectId);
      projects = projects.filter(project => project.id !== projectId);
      return true;
    },
    async openProject(projectId) {
      return projects.find(project => project.id === projectId) || null;
    },
    async saveCurrentProject() {
      return true;
    }
  };

  const container = new FakeElement('section');
  const owner = new ProjectPageCapabilityOwner(container, {
    adapter,
    showToast(message, type) {
      toasts.push([message, type]);
    },
    createModal(options) {
      const overlay = new FakeElement('div');
      overlay.modalOptions = options;
      overlay.content = options.content;
      overlay.footer = options.footer;
      modals.push(overlay);
      return overlay;
    },
    closeModal(overlay) {
      overlay.closed = true;
    },
    createButton(options) {
      const button = new FakeElement('button');
      button.textContent = options.text || '';
      button.buttonType = options.type || 'primary';
      if (typeof options.onClick === 'function') {
        button.addEventListener('click', options.onClick);
      }
      return button;
    }
  });

  owner.projects = [...initialProjects];
  return { adapter, container, modals, owner, toasts };
}

function clickOwnerAction(owner, actionElement) {
  let prevented = false;
  owner.handleClick({
    target: actionElement,
    preventDefault() {
      prevented = true;
    }
  });
  assert.equal(prevented, true);
}

test('ProjectPageCapabilityOwner source does not use browser prompt or confirm', () => {
  const source = readOwnerSource();

  assert.doesNotMatch(source, /globalThis\.(prompt|confirm)|window\.(prompt|confirm)/);
});

test('ProjectPageCapabilityOwner clicking new project opens modal without globalThis.prompt', (t) => {
  const { modals, owner } = createHarness(t);

  clickOwnerAction(owner, createAction('new'));

  assert.equal(modals.length, 1);
  assert.equal(modals[0].modalOptions.title, '新建工程');
  assert.ok(modals[0].content.querySelector('[data-project-name-input]'));
  assert.ok(modals[0].content.querySelector('[data-project-desc-input]'));
});

test('ProjectPageCapabilityOwner keeps create modal open and skips adapter when name is empty', async (t) => {
  const { adapter, modals, owner, toasts } = createHarness(t);

  owner.createProject();
  const modal = modals.at(-1);
  const createButton = modal.footer.find(button => button.dataset.projectModalAction === 'create');

  await createButton.click();

  const error = modal.content.querySelector('[data-project-name-error]');
  assert.deepEqual(adapter.createProjectCalls, []);
  assert.equal(modal.closed, undefined);
  assert.equal(error.hidden, false);
  assert.equal(error.textContent, '请输入工程名称');
  assert.deepEqual(toasts.at(-1), ['请输入工程名称', 'warning']);
});

test('ProjectPageCapabilityOwner submits name and description then refreshes list', async (t) => {
  const { adapter, modals, owner, toasts } = createHarness(t);

  owner.createProject();
  const modal = modals.at(-1);
  modal.content.querySelector('[data-project-name-input]').value = '产线检测工程';
  modal.content.querySelector('[data-project-desc-input]').value = '用于 A 线 AOI 检测';
  const createButton = modal.footer.find(button => button.dataset.projectModalAction === 'create');

  await createButton.click();

  assert.deepEqual(adapter.createProjectCalls, [['产线检测工程', '用于 A 线 AOI 检测']]);
  assert.equal(adapter.listProjectCalls.length, 1);
  assert.equal(owner.projects[0].name, '产线检测工程');
  assert.equal(modal.closed, true);
  assert.deepEqual(toasts.at(-1), ['工程 "产线检测工程" 已创建', 'success']);
});

test('ProjectPageCapabilityOwner delete action opens confirm modal without globalThis.confirm', async (t) => {
  const project = {
    id: 'project-delete-1',
    name: '待删除工程',
    description: '',
    createdAt: '2026-07-08T00:00:00.000Z'
  };
  const { adapter, modals, owner, toasts } = createHarness(t, [project]);
  const item = new FakeElement('article');
  item.dataset.projectId = project.id;
  const deleteAction = createAction('delete', item);

  clickOwnerAction(owner, deleteAction);

  const modal = modals.at(-1);
  assert.equal(modal.modalOptions.title, '确认删除');
  assert.equal(modal.content.querySelector('[data-project-delete-name]').textContent, project.name);
  assert.equal(modal.content.querySelector('[data-project-delete-id]').textContent, project.id);

  const confirmButton = modal.footer.find(button => button.dataset.projectModalAction === 'confirm-delete');
  await confirmButton.click();

  assert.deepEqual(adapter.deleteProjectCalls, [project.id]);
  assert.equal(adapter.listProjectCalls.length, 1);
  assert.equal(modal.closed, true);
  assert.deepEqual(toasts.at(-1), [`工程 "${project.name}" 已删除`, 'success']);
});
