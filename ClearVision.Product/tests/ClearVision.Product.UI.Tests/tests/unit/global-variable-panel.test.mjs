import test from 'node:test';
import assert from 'node:assert/strict';

function installDom() {
  const container = {
    innerHTML: '',
    querySelector() {
      return null;
    },
    querySelectorAll() {
      return [];
    }
  };

  global.window = {
    prompt() {
      return null;
    },
    alert(message) {
      global.__lastAlert = message;
    },
    location: {
      protocol: 'http:',
      hostname: 'localhost',
      port: '5000'
    },
    localStorage: {
      getItem() { return null; },
      setItem() {},
      removeItem() {}
    }
  };
  global.document = {
    title: '',
    getElementById(id) {
      return id === 'global-variables-root' ? container : null;
    },
    querySelector() {
      return null;
    },
    createElement() {
      return {
        type: '',
        className: '',
        id: '',
        textContent: '',
        addEventListener() {}
      };
    },
    addEventListener() {}
  };
  global.localStorage = global.window.localStorage;
  Object.defineProperty(global, 'crypto', {
    configurable: true,
    value: {
    randomUUID() {
      global.__uuidCounter = (global.__uuidCounter ?? 0) + 1;
      return `00000000-0000-0000-0000-${String(global.__uuidCounter).padStart(12, '0')}`;
    }
    }
  });
  global.__lastAlert = '';
  global.__uuidCounter = 0;
  return container;
}

test('GlobalVariablePanel renders valid controls and edits schema metadata', async () => {
  const container = installDom();
  const { default: projectManager } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/project/projectManager.js');
  const { default: GlobalVariablePanel } = await import('../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/global-variables/globalVariablePanel.js');

  const project = {
    id: 'project-1',
    name: 'Project',
    flow: {
      operators: [
        {
          id: 'op-1',
          name: 'Counter',
          outputPorts: [
            { id: 'port-image', name: 'Image', dataType: 'Image' },
            { id: 'port-1', name: 'Count', dataType: 'Integer' }
          ]
        }
      ]
    },
    globalVariables: {
      schemaVersion: '1.0',
      variables: [],
      sourceBindings: [],
      targetBindings: []
    }
  };
  projectManager.currentProject = project;

  const panel = new GlobalVariablePanel('global-variables-root');
  panel.project = project;
  panel.schema = project.globalVariables;
  panel.values = [];
  panel.render();

  assert.match(container.innerHTML, /id="gv-add"/);
  assert.match(container.innerHTML, /Reset values<\/button>/);

  const prompts = ['judge.expected_count', 'Int64', '4'];
  global.window.prompt = () => prompts.shift();
  panel.addVariable();

  assert.equal(panel.schema.variables.length, 1);
  assert.equal(panel.schema.variables[0].name, 'judge.expected_count');
  assert.equal(panel.schema.variables[0].initialValue, 4);
  assert.equal(projectManager.currentProject.globalVariables.variables.length, 1);

  global.window.prompt = () => '1';
  panel.configureSourceBinding();

  assert.equal(panel.schema.sourceBindings.length, 1);
  assert.equal(panel.schema.sourceBindings[0].operatorId, 'op-1');
  assert.equal(panel.schema.sourceBindings[0].outputPortId, 'port-1');

  panel.deleteVariable(panel.schema.variables[0].id);
  assert.equal(panel.schema.variables.length, 1);
  assert.match(global.__lastAlert, /still referenced/);
});
