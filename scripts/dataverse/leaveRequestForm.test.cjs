const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const source = fs.readFileSync(__dirname + '/cr2c9_leaveRequestForm.js', 'utf8');
function setup({ manager = 'user', superior = 'manager', baseline = 0, failure = false, self = false, hr = false, leaveType = null } = {}) {
  let currentManager = manager, currentSuperior = superior, saved = baseline, failing = failure, version = '100';
  const controls = [];
  const attrs = {};
  for (const [name, value] of Object.entries({ cr2c9_status: baseline, cr2c9_employeename: [{ id: 'employee', entityType: 'cr2c9_employee' }], cr2c9_decisioncomments: null, cr2c9_decisionbyentraid: null, cr2c9_decisionbyname: null, cr2c9_decisionon: null, cr2c9_decisionexpectedversion: null })) {
    const c = { disabled: false, options: [], setDisabled(v) { this.disabled = v; }, clearOptions() { this.options = []; }, addOption(o) { this.options.push(o.value); } };
    controls.push(c);
    attrs[name] = { value, dirty: false, controls: [c], getValue() { return this.value; }, setValue(v) { this.value = v; this.dirty = true; }, getOptions() { return [0,1,2,3,4,5].map(value => ({value})); }, addOnChange(fn) { this.change = fn; }, setSubmitMode(v) { this.submit = v; } };
  }
  attrs.cr2c9_leavetypename = { value: leaveType ? [{ name: leaveType }] : null, getValue() { return this.value; }, addOnChange(fn) { this.change = fn; } };
  for (const name of ['cr2c9_compassionaterelationship', 'cr2c9_marriageregisteredon', 'cr2c9_firstlegalmarriage', 'cr2c9_expectedconfinementdate', 'cr2c9_maternityformalnoticedate', 'cr2c9_paternityexpecteddate', 'cr2c9_paternitybirthdate', 'cr2c9_paternitynoticedate', 'cr2c9_trainingcategory', 'cr2c9_maternityhrverifiedon', 'cr2c9_paternityhrverifiedon']) {
    attrs[name] = { controls: [{ visible: true, setVisible(value) { this.visible = value; }, setDisabled() {} }] };
  }
  let notification;
  const form = { getAttribute: n => attrs[n], ui: { getFormType: () => 2, controls, setFormNotification: text => notification = text, clearFormNotification: () => notification = null }, data: { addOnLoad(fn) { this.load = fn; }, entity: { getId: () => 'request', getIsDirty: () => Object.values(attrs).some(a => a.dirty), addOnPostSave(fn) { this.post = fn; } } } };
  const Xrm = { Utility: { getGlobalContext: () => ({ userSettings: { userId: '{system-user}', roles: { getAll: () => hr ? [{ name: 'Leave HR' }] : [] } } }) }, WebApi: { async retrieveRecord(table,id) {
    if (failing) throw new Error('Retrieval failed');
    if (table === 'systemuser') { assert.equal(id, 'system-user'); return { azureactivedirectoryobjectid: 'user', fullname: 'Manager Name' }; }
    if (table === 'cr2c9_leaverequest') return { cr2c9_status: saved, _cr2c9_employeename_value: 'employee', '@odata.etag': version === null ? null : 'W/"' + version + '"' };
    if (id === 'employee') return { _cr2c9_superior_value: currentSuperior, cr2c9_entrauserid: self ? 'user' : 'requestor' };
    return { cr2c9_entrauserid: currentManager };
  } } };
  const sandbox = { Xrm, setTimeout, clearTimeout }; vm.createContext(sandbox); vm.runInContext(source, sandbox);
  const api = sandbox.LeaveRequestForm;
  const args = { blocked: false, protected: false, preventDefault() { this.blocked = true; }, preventDefaultOnError() { this.protected = true; } };
  return { api, attrs, form, args, context: { getFormContext: () => form, getEventArgs: () => args }, settle: () => new Promise(r => setImmediate(r)), changeManager(v) { currentManager = v; }, clearSuperior() { currentSuperior = null; }, decideElsewhere() { saved = 1; }, changeVersion(v) { version = v; }, fail() { failing = true; }, notification: () => notification };
}
for (const target of [1,3]) test('matching Superior saves and stamps status ' + target, async () => {
  const s = setup(); s.api.onLoad(s.context); await s.settle();
  assert.equal(s.attrs.cr2c9_status.controls[0].disabled, false);
  assert.deepEqual(s.attrs.cr2c9_status.controls[0].options, [0,1,3]);
  s.attrs.cr2c9_status.setValue(target); await s.api.onSave(s.context);
  assert.equal(s.args.protected, true); assert.equal(s.args.blocked, false);
  assert.equal(s.attrs.cr2c9_decisionbyentraid.value, 'user'); assert.equal(s.attrs.cr2c9_decisionbyname.value, 'Manager Name');
  assert.ok(s.attrs.cr2c9_decisionon.value); assert.equal(s.attrs.cr2c9_decisionon.submit, 'always');
  assert.equal(s.attrs.cr2c9_decisionexpectedversion.value, '100');
  assert.equal(s.attrs.cr2c9_decisionexpectedversion.submit, 'always');
  s.form.data.entity.post({ getEventArgs: () => ({ getIsSaveSuccess: () => true }) });
  assert.equal(s.attrs.cr2c9_status.controls[0].disabled, true);
});
test('HR can decide pending leave without an assigned Superior', async () => {
  const s = setup({ hr: true, superior: null, manager: 'other' });
  s.api.onLoad(s.context); await s.settle();
  assert.equal(s.attrs.cr2c9_status.controls[0].disabled, false);
  s.attrs.cr2c9_status.setValue(1); await s.api.onSave(s.context);
  assert.equal(s.args.blocked, false);
});
test('HR cannot decide own leave in the form', async () => {
  const s = setup({ hr: true, self: true });
  s.api.onLoad(s.context); await s.settle();
  assert.equal(s.attrs.cr2c9_status.controls[0].disabled, true);
});
for (const [label, config] of [['another user',{manager:'other'}],['blank Superior',{superior:null}],['retrieval failure',{failure:true}],['self approval',{self:true}],['decided',{baseline:1}]]) test(label + ' stays locked and blocks changes', async () => {
  const s = setup(config); s.api.onLoad(s.context); await s.settle(); assert.equal(s.attrs.cr2c9_status.controls[0].disabled,true);
  s.attrs.cr2c9_status.setValue(3); s.api.onSave(s.context); assert.equal(s.args.blocked,true); assert.equal(s.attrs.cr2c9_decisionon.value,null);
});
for (const [label, action] of [['manager changed',s=>s.changeManager('other')],['Superior cleared',s=>s.clearSuperior()],['retrieval failed',s=>s.fail()],['already decided remotely',s=>s.decideElsewhere()]]) test('save rechecks: ' + label, async () => {
  const s = setup(); s.api.onLoad(s.context); await s.settle(); s.attrs.cr2c9_status.setValue(1); action(s);
  await assert.rejects(s.api.onSave(s.context)); assert.equal(s.args.protected,true); assert.equal(s.attrs.cr2c9_decisionon.value,null);
});
test('legacy Pending HR remains visible and locked', async () => { const s=setup({baseline:4}); s.api.onLoad(s.context); await s.settle(); assert.ok(s.attrs.cr2c9_status.controls[0].options.includes(4)); assert.equal(s.attrs.cr2c9_status.controls[0].disabled,true); });
test('missing stamp column blocks save', () => { const s=setup(); delete s.attrs.cr2c9_decisionbyentraid; s.api.onLoad(s.context); s.api.onSave(s.context); assert.equal(s.args.blocked,true); });
test('changed request version blocks decision without stamps', async () => {
  const s=setup(); s.api.onLoad(s.context); await s.settle(); s.attrs.cr2c9_status.setValue(1); s.changeVersion('101');
  await assert.rejects(s.api.onSave(s.context)); assert.equal(s.attrs.cr2c9_decisionon.value,null); assert.equal(s.attrs.cr2c9_decisionexpectedversion.value,null);
});
test('missing saved version fails closed', async () => {
  const s=setup(); s.changeVersion(null); s.api.onLoad(s.context); await s.settle();
  assert.equal(s.attrs.cr2c9_status.controls[0].disabled,true); s.attrs.cr2c9_status.setValue(1); s.api.onSave(s.context); assert.equal(s.args.blocked,true);
});
test('missing version form input blocks save', () => {
  const s=setup(); delete s.attrs.cr2c9_decisionexpectedversion; s.api.onLoad(s.context); s.api.onSave(s.context); assert.equal(s.args.blocked,true);
});
const categoryFields = {
  'Compassionate Leave': ['cr2c9_compassionaterelationship'],
  'Marriage Leave': ['cr2c9_marriageregisteredon', 'cr2c9_firstlegalmarriage'],
  'Maternity Leave': ['cr2c9_expectedconfinementdate', 'cr2c9_maternityformalnoticedate'],
  'Paternity Leave': ['cr2c9_paternityexpecteddate', 'cr2c9_paternitybirthdate', 'cr2c9_paternitynoticedate'],
  'Training Leave': ['cr2c9_trainingcategory']
};
const specialFields = Object.values(categoryFields).flat();
for (const leaveType of ['Annual Leave', 'Medical Leave', 'Hospitalisation Leave', 'Emergency Leave', 'Unpaid Leave', ...Object.keys(categoryFields)]) {
  test(`${leaveType} shows only its relevant review fields`, () => {
    const s = setup({ leaveType });
    s.api.onLoad(s.context);
    for (const field of specialFields) assert.equal(s.attrs[field].controls[0].visible, (categoryFields[leaveType] || []).includes(field), field);
    for (const field of ['cr2c9_maternityhrverifiedon', 'cr2c9_paternityhrverifiedon']) assert.equal(s.attrs[field].controls[0].visible, false, field);
  });
}
test('switching leave type updates field visibility', () => {
  const s = setup({ leaveType: 'Medical Leave' });
  s.api.onLoad(s.context);
  s.attrs.cr2c9_leavetypename.value = [{ name: 'Marriage Leave' }];
  s.attrs.cr2c9_leavetypename.change();
  assert.equal(s.attrs.cr2c9_marriageregisteredon.controls[0].visible, true);
  assert.equal(s.attrs.cr2c9_compassionaterelationship.controls[0].visible, false);
});
