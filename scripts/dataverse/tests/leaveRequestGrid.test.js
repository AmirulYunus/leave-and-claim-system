const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const code = fs.readFileSync(path.join(__dirname, '..', 'cr2c9_leaveRequestGrid.js'), 'utf8');
const calls = [];
let failUpdate = false;
const context = {
  Xrm: {
    WebApi: {
      retrieveRecord: async (table, id) => {
        calls.push(['retrieve', table, id]);
        return { cr2c9_leaverequestnumber: 'LR-TEST', cr2c9_status: 0, '@odata.etag': 'W/"42"' };
      },
      updateRecord: async (table, id, values) => {
        calls.push(['update', table, id, values]);
        if (failUpdate) throw new Error('Approval was blocked by the server.');
      },
    },
    Navigation: {
      openConfirmDialog: async (options) => {
        calls.push(['confirm', options]);
        return { confirmed: true };
      },
      openAlertDialog: async (options) => calls.push(['alert', options]),
    },
  },
};
vm.createContext(context);
vm.runInContext(code, context);

async function main() {
  const grid = { refresh: () => calls.push(['refresh']) };
  await context.LeaveRequestGrid.approve(['{11111111-1111-1111-1111-111111111111}'], grid);
  assert.equal(calls[2][0], 'update');
  assert.equal(calls[2][3].cr2c9_status, 1);
  assert.equal(calls[2][3].cr2c9_decisionexpectedversion, '42');
  assert.match(calls[1][1].text, /LR-TEST/);
  assert.equal(calls[3][0], 'refresh');

  calls.length = 0;
  await context.LeaveRequestGrid.reject(['{22222222-2222-2222-2222-222222222222}'], grid);
  assert.equal(calls[2][3].cr2c9_status, 3);

  calls.length = 0;
  await context.LeaveRequestGrid.approve([], grid);
  assert.equal(calls.length, 1);
  assert.match(calls[0][1].text, /Select exactly one/);

  calls.length = 0;
  await context.LeaveRequestGrid.approve(['11111111-1111-1111-1111-111111111111'], {
    refresh: () => { throw new Error('Grid unavailable'); },
  });
  assert.equal(calls[2][0], 'update');
  assert.match(calls[3][1].text, /approved.*Refresh the list/);
  assert.notEqual(calls[3][1].title, 'Decision not saved');

  calls.length = 0;
  failUpdate = true;
  await context.LeaveRequestGrid.reject(['11111111-1111-1111-1111-111111111111'], {
    refresh: () => { throw new Error('Grid unavailable'); },
  });
  assert.equal(calls[3][1].title, 'Decision not saved');
  assert.match(calls[3][1].text, /blocked by the server/);
  console.log('PASS Superior grid decisions distinguish save failures from refresh failures');
}
main().catch(error => { console.error(error); process.exitCode = 1; });
