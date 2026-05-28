import test from 'node:test';
import assert from 'node:assert/strict';

test('station monitor refresh tick marks time-derived UI dirty without forcing result workbench redraw', async () => {
  const { StationMonitorView } = await import(
    '../../../../src/Acme.Product.Desktop/wwwroot/src/features/stations/stationMonitorView.js'
  );
  const previousWindow = global.window;
  const previousDocument = global.document;
  let intervalCallback = null;
  let requested = 0;

  global.window = {
    setInterval(callback) {
      intervalCallback = callback;
      return 42;
    },
    clearInterval() {}
  };
  global.document = { hidden: false };

  try {
    const view = Object.create(StationMonitorView.prototype);
    view.isActive = true;
    view.refreshTimer = null;
    view._renderDirty = false;
    view._resultsDirty = false;
    view._renderQueued = false;
    view.requestRender = function requestRender() {
      requested += 1;
    };

    view.startRefreshTimer();
    assert.equal(typeof intervalCallback, 'function');

    intervalCallback();

    assert.equal(view._renderDirty, true);
    assert.equal(view._resultsDirty, false);
    assert.equal(requested, 1);
  } finally {
    global.window = previousWindow;
    global.document = previousDocument;
  }
});

test('station monitor caches online state while sorting stations within one render', async () => {
  const { StationMonitorView } = await import(
    '../../../../src/Acme.Product.Desktop/wwwroot/src/features/stations/stationMonitorView.js'
  );

  const view = Object.create(StationMonitorView.prototype);
  view._renderContextActive = true;
  view._stationEntriesCache = null;
  view.stations = new Map([
    ['b', { stationId: 'b', state: 'Idle', online: true }],
    ['a', { stationId: 'a', state: 'Idle', online: true }],
    ['c', { stationId: 'c', state: 'Idle', online: false }]
  ]);

  let calls = 0;
  view.computeIsOnline = (station) => {
    calls += 1;
    return station.online;
  };

  const firstEntries = view.getStationRenderEntries();
  const secondEntries = view.getStationRenderEntries();

  assert.equal(calls, 3);
  assert.deepEqual(firstEntries.map((entry) => entry.station.stationId), ['a', 'b', 'c']);
  assert.deepEqual(secondEntries.map((entry) => entry.station.stationId), ['a', 'b', 'c']);
});
