const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
function fixture(execute, retrieveMultipleRecords = async () => ({entities:[]})) {
  const notices = new Map(), changes = {}, loads = [];
  let dirty = false, id = '{a3a2b544-41de-4385-bacf-9859c45656c5}';
  const form = { data: { entity: { getId: () => id, getIsDirty: () => dirty }, addOnLoad: fn => loads.push(fn) },
    getAttribute: name => ({ addOnChange: fn => { (changes[name] ||= []).push(fn); } }),
    getControl: () => ({ setDisabled() {} }), ui: { setFormNotification: (message, level, key) => notices.set(key,{message,level}) } };
  const context = vm.createContext({ Xrm: { WebApi: { online: { execute, retrieveMultipleRecords } } }, setTimeout, clearTimeout, module: { exports: {} } });
  vm.runInContext(fs.readFileSync(__dirname + '/cr2c9_employeeEntitlement.js','utf8'),context);
  return { api:context.module.exports, context:{getFormContext:()=>form}, notices, changes, loads,
    setDirty:value=>dirty=value, setId:value=>id=value, form, message:()=>notices.get('leave-entitlement-preview').message };
}
const complete = { AssessmentDate:'2026-10-09', Annual:{Status:'Calculated',Days:16}, Sick:{Status:'Calculated',Days:22}, Hospitalisation:{Status:'Calculated',Days:60} };
const response = p => ({ok:true,json:async()=>({PreviewJson:JSON.stringify(p)})});
test('calls bound read-only API and labels rates separately from balances',async()=>{
  let request; const f=fixture(async r=>{request=r;return response(complete)}); await f.api.onLoad(f.context);
  assert.equal(request.getMetadata().operationType,1); assert.equal(request.getMetadata().boundParameter,'entity');
  assert.equal(request.entity.id,'a3a2b544-41de-4385-bacf-9859c45656c5'); assert.match(f.message(),/Annual: 16 days/); assert.match(f.message(),/not an available balance/);
});
test('company Hospitalisation figure subtracts recorded Medical and Hospitalisation usage',async()=>{
  const f=fixture(async()=>response(complete),async table=>({entities:table==='cr2c9_leavebalanceperiod'
    ? [{cr2c9_leavebalanceperiodid:'f72ae090-5949-45fc-ae94-888af952bec1',cr2c9_periodstart:'2026-01-01',cr2c9_periodend:'2026-12-31'}]
    : [{cr2c9_entrykind:290040000,cr2c9_hours:112,cr2c9_operationkey:'grant'},
      {cr2c9_entrykind:290040001,cr2c9_hours:-8,cr2c9_operationkey:'approved'}]}));
  await f.api.onLoad(f.context);
  assert.match(f.message(),/60-day combined figure: 58 days after 1 Medical and 1 Hospitalisation days used/);
  assert.match(f.message(),/not an approval limit/);
});
test('missing balance periods never display an invented company balance',async()=>{
  const f=fixture(async()=>response(complete)); await f.api.onLoad(f.context);
  assert.match(f.message(),/awaits recorded Medical and Hospitalisation balance periods/);
  assert.doesNotMatch(f.message(),/60-day combined figure: 60 days/);
});
test('missing inputs show Pending HR details without zero',async()=>{
  const pending={Status:'PendingHrDetails',Reason:'Pending HR details: Date Joined',Days:null};
  const f=fixture(async()=>response({...complete,Annual:pending})); await f.api.onLoad(f.context);
  assert.match(f.message(),/Pending HR details/); assert.doesNotMatch(f.message(),/Annual: 0 days/);
});
test('new record requires save and never calls API',async()=>{
  const f=fixture(async()=>{throw Error('must not call')}); f.setId(''); await f.api.onLoad(f.context); assert.match(f.message(),/Save this employee/);
});
test('dirty HR input defers server preview until saved',async()=>{
  const f=fixture(async()=>{throw Error('must not call')}); f.setDirty(true); await f.api.onLoad(f.context); assert.match(f.message(),/Save HR/);
});
test('handlers are installed once per form',async()=>{
  const f=fixture(async()=>response(complete)); await f.api.onLoad(f.context); await f.api.onLoad(f.context);
  assert.equal(f.loads.length,1); assert.equal(f.changes.cr2c9_employeelevel.length,1);
});
test('HR change invalidates a result already in flight',async()=>{
  let finish; const f=fixture(()=>new Promise(r=>finish=r)); const load=f.api.onLoad(f.context);
  f.changes.cr2c9_employeelevel[0](); finish(response(complete)); await load; assert.match(f.message(),/Save HR/);
});
test('post-save data load refreshes rates',async()=>{
  let calls=0; const f=fixture(async()=>{calls++; return response(complete)}); await f.api.onLoad(f.context);
  await f.loads[0](); assert.equal(calls,2);
});
test('failed API shows unavailable without changing entitlement',async()=>{
  const f=fixture(async()=>{throw Error('denied')}); await f.api.onLoad(f.context); assert.match(f.message(),/unavailable/); assert.match(f.message(),/no entitlement or balance was changed/);
});
test('malformed successful response cannot display an invented allowance',async()=>{
  const f=fixture(async()=>response({...complete,Annual:{Status:'Calculated',Days:null}})); await f.api.onLoad(f.context); assert.match(f.message(),/unavailable/);
});
