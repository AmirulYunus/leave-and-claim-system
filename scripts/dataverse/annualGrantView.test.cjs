const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

const html = fs.readFileSync(__dirname + '/cr2c9_annualGrant.html', 'utf8');
const script = html.match(/<script>([\s\S]*?)<\/script>/)?.[1];
assert.ok(script, 'annual balance script is embedded');
const employeeId = 'adae5971-d3c2-f111-a05a-000d3a826b69';
const periodId = '381fe531-358f-4839-99a5-ef5620d2deb7';
const elements = Object.fromEntries(['grant', 'refresh', 'result', 'balance', 'carryForward', 'priorYearDetails', 'priorYearSummary', 'hrPreviewSection', 'carryHeading', 'carryIntro', 'carryAvailability', 'hrPreview', 'confirmCarry', 'previewUpcoming', 'upcomingPreview', 'exceptionSection', 'exceptionDays', 'exceptionReference', 'exceptionStatus', 'recordException', 'adjustSection', 'reviewedDays', 'adjustReason', 'adjustDocument', 'grantHospital', 'hospitalResult'].map(id => [id, {
  disabled: false, hidden: id === 'hrPreviewSection' || id === 'confirmCarry', className: '', textContent: '', handlers: {},
  addEventListener(name, handler) { this.handlers[name] = handler; }
}]));
let fakeToday = '2026-10-14T02:00:00Z';
const context = vm.createContext({
  document: { getElementById: id => elements[id] }, Intl, Number, Error,
  Date: class extends Date { constructor(...args) { super(...(args.length ? args : [fakeToday])); } },
  confirm: () => true,
});
context.window = context;
vm.runInContext(script + '\nglobalThis.testView = { refreshBalance, summarize, previewThisYearCarryForward };', context);

async function read(periods, entries, reviews = [], credits = [], preview = null, exceptions = []) {
  let confirmations = 0;
  const api = { WebApi: { online: {
    async retrieveMultipleRecords(table, options) {
      assert.match(options, /\$filter=/);
      if (table === 'cr2c9_leavebalanceperiod') {
        assert.match(options, new RegExp(employeeId));
        return { entities: periods };
      }
      if (table === 'cr2c9_leavebalanceentry') {
        assert.match(options, /_cr2c9_balanceperiod_value eq /);
        return { entities: entries };
      }
      assert.match(options, /_cr2c9_sourceperiod_value eq /);
      if (table === 'cr2c9_carryforwardreview') return { entities: reviews };
      if (table === 'cr2c9_carryforwardexception') return { entities: exceptions };
      assert.equal(table, 'cr2c9_carryforwardcredit');
      return { entities: credits };
    },
    async execute(request) {
      const operation = request.getMetadata().operationName;
      if (operation === 'cr2c9_ConfirmAnnualCarryForward') {
        confirmations++;
        preview.Status = 'AlreadyConfirmed';
        return { ok: true, async json() { return { Status: 'Confirmed', CreditedHours: 40, SeparateUnusedHours: 24 }; } };
      }
      assert.equal(operation, 'cr2c9_PreviewAnnualCarryForward');
      return preview ? { ok: true, async json() { return preview; } } : { ok: false };
    }
  } } };
  context.setGrantContext(api, { data: { entity: { getId: () => employeeId } } });
  await context.testView.refreshBalance();
  return { balance: elements.balance.textContent, carry: elements.carryForward.textContent,
    hr: elements.hrPreview.textContent, hrHidden: elements.hrPreviewSection.hidden,
    heading: elements.carryHeading.textContent, priorHidden: elements.priorYearDetails.hidden,
    confirmHidden: elements.confirmCarry.hidden, confirm: () => elements.confirmCarry.handlers.click(),
    upcomingHidden: elements.previewUpcoming.hidden, upcoming: () => elements.previewUpcoming.handlers.click(),
    upcomingText: () => elements.upcomingPreview.textContent, availabilityText: () => elements.carryAvailability.textContent,
    confirmationCount: () => confirmations };
}

(async () => {
  const period = [{ cr2c9_leavebalanceperiodid: periodId, cr2c9_name: 'SYNTHETIC cancellation test 2026',
    cr2c9_periodstart: '2026-01-01', cr2c9_periodend: '2026-12-31' }];
  const grant = { cr2c9_entrykind: 290040000, cr2c9_hours: 8 };
  const debit = { cr2c9_entrykind: 290040001, cr2c9_hours: -8 };
  const reversal = { cr2c9_entrykind: 290040002, cr2c9_hours: 8 };
  let { balance: output, carry } = await read(period, [grant, debit]);
  assert.match(output, /Development test balance/);
  assert.match(output, /Recorded grants: 8 hours \(1 day\)/);
  assert.match(output, /Recorded remaining \(may include unearned leave\): 0 hours \(0 days\)/);
  assert.equal(carry, '', 'an unrelated missing prior year does not distract from the current-year preview');
  assert.equal(elements.priorYearDetails.hidden, true);
  ({ balance: output } = await read(period, [grant, debit, reversal]));
  assert.match(output, /Approved leave used \(after cancellation reversals\): 0 hours/);
  assert.match(output, /Recorded remaining \(may include unearned leave\): 8 hours \(1 day\)/);
  ({ balance: output } = await read([], []));
  assert.match(output, /No Annual Leave balance period has been granted/);
  assert.doesNotMatch(output, /Recorded remaining.*0 hours/);
  ({ balance: output } = await read(period, [{ cr2c9_entrykind: 290040000, cr2c9_hours: -8 }]));
  assert.match(output, /Balance could not be read/);
  const prior = { cr2c9_leavebalanceperiodid: '0aa6ad79-f6c4-f111-a05a-000d3a826b69',
    cr2c9_periodstart: '2025-01-01', cr2c9_periodend: '2025-12-31' };
  ({ carry } = await read([...period, prior], [grant], [{ cr2c9_reviewstatus: 290040001,
    cr2c9_originalcarryhours: 8, cr2c9_originalunusedhours: 0,
    cr2c9_reviewedcarryhours: 8, cr2c9_reviewedunusedhours: 0 }]));
  assert.match(carry, /HR review: Pending HR review/);
  assert.match(carry, /No separate carry-forward credit is posted/);
  ({ carry } = await read([...period, prior], [grant], [{ cr2c9_reviewstatus: 290040002,
    cr2c9_originalcarryhours: 8, cr2c9_originalunusedhours: 0,
    cr2c9_reviewedcarryhours: 8, cr2c9_reviewedunusedhours: 0 }],
  [{ cr2c9_hours: 8, cr2c9_useby: '2026-03-31' }]));
  assert.match(carry, /Posted separate carry-forward credit: 8 hours/);
  const verified = await read([...period, prior], [grant], [], [], {
    Status: 'ReadyForHrReview', SourceYear: 2025, SourceUnusedHours: 64,
    StandardLimitHours: 40, CandidateCarryHours: 40, SeparateUnusedHours: 24
  });
  assert.equal(verified.hrHidden, false);
  assert.equal(verified.heading, '2026 carry-forward estimate');
  assert.equal(verified.hr, '', 'the current-year card does not lead with the prior-year proposal');
  assert.equal(verified.confirmHidden, false, 'HR can click to see the date block');
  await verified.confirm();
  assert.match(verified.availabilityText(), /cannot confirm 2026 carry-forward until 1 January 2027/);
  assert.equal(verified.confirmationCount(), 0, 'early confirmation never calls the posting action');
  assert.equal(verified.upcomingHidden, false);
  await verified.upcoming();
  assert.match(verified.upcomingText(), /Estimated to carry forward: 8 hours \(1 day\)/);
  const hiddenPreview = await read(period, [grant]);
  assert.equal(hiddenPreview.hrHidden, false, 'a reader can see the provisional current-year preview');
  assert.equal(hiddenPreview.upcomingHidden, false);
  assert.equal(hiddenPreview.confirmHidden, true, 'non-HR callers cannot see the final confirmation action');
  await hiddenPreview.upcoming();
  assert.match(hiddenPreview.upcomingText(), /Estimated to carry forward: 8 hours \(1 day\)/);
  assert.match(hiddenPreview.availabilityText(), /A Leave HR reviewer can confirm/);
  fakeToday = '2027-01-10T02:00:00Z';
  const current = { cr2c9_leavebalanceperiodid: '6777ad79-f6c4-f111-a05a-000d3a826b69',
    cr2c9_periodstart: '2027-01-01', cr2c9_periodend: '2027-12-31' };
  const closed = { ...prior, cr2c9_periodstart: '2026-01-01', cr2c9_periodend: '2026-12-31' };
  const january = await read([current, closed], [grant], [], [], {
    Status: 'ReadyForHrReview', SourceYear: 2026, SourceUnusedHours: 64,
    StandardLimitHours: 40, CandidateCarryHours: 40, SeparateUnusedHours: 24
  });
  assert.equal(january.confirmHidden, false, 'HR can review the last closed year in January');
  await january.confirm();
  assert.equal(january.confirmationCount(), 1, 'the HR button invokes confirmation once');
  assert.equal(elements.confirmCarry.hidden, true, 'a confirmed review cannot be resubmitted');
  console.log('Annual balance and HR carry-forward confirmation UI checks passed.');
})().catch(error => { console.error(error); process.exitCode = 1; });
